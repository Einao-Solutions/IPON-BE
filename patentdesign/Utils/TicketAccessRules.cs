using System.Security.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Bson;
using MongoDB.Driver;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

public sealed record TicketViewer
{
    public string UserId { get; private init; } = string.Empty;
    public string CreatorId { get; private init; } = string.Empty;
    public string DisplayName { get; private init; } = string.Empty;
    public bool CanViewAll { get; private init; }
    public IReadOnlyList<TicketCategory> RegistryCategories { get; private init; } = Array.Empty<TicketCategory>();

    private TicketViewer() { }

    public static TicketViewer FromPrincipal(ClaimsPrincipal principal, AppUser? account = null)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            throw new AuthenticationException("An authenticated user identifier is required.");
        }

        if (account != null && account.Id != userId)
        {
            throw new AuthenticationException("The account must match the authenticated user.");
        }

        var categories = new List<TicketCategory>();
        if (principal.IsInRole(nameof(Roles.TrademarkSupport))) categories.Add(TicketCategory.TrademarkRegistry);
        if (principal.IsInRole(nameof(Roles.PatentDesignSupport))) categories.Add(TicketCategory.PatentDesignRegistry);
        return new TicketViewer
        {
            UserId = userId,
            CreatorId = string.IsNullOrWhiteSpace(account?.CreatorId) ? userId : account.CreatorId,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue(ClaimTypes.Email) ?? userId,
            CanViewAll = principal.IsInRole(nameof(Roles.Tech)) || principal.IsInRole(nameof(Roles.SuperAdmin)),
            RegistryCategories = categories.AsReadOnly()
        };
    }

    public bool OwnsTicket(string? creatorId) =>
        !string.IsNullOrWhiteSpace(creatorId) && (creatorId == UserId || creatorId == CreatorId);
}

public static class TicketAccessRules
{
    public static FilterDefinition<TicketInfo> CreatorFilter(string? creatorId, TicketViewer? viewer)
    {
        var filter = Builders<TicketInfo>.Filter;
        if (string.IsNullOrWhiteSpace(creatorId) || creatorId == "null") return filter.Empty;

        if (viewer != null && viewer.OwnsTicket(creatorId) && viewer.CreatorId != viewer.UserId)
        {
            return filter.In(x => x.creatorId, new[] { viewer.CreatorId, viewer.UserId });
        }

        return filter.Eq(x => x.creatorId, creatorId);
    }

    public static FilterDefinition<TicketInfo> Filter(TicketViewer? viewer)
    {
        var filter = Builders<TicketInfo>.Filter;
        if (viewer == null || viewer.CanViewAll) return filter.Empty;

        var access = filter.Eq(x => x.creatorId, viewer.UserId);
        if (viewer.CreatorId != viewer.UserId)
        {
            access |= filter.Eq(x => x.creatorId, viewer.CreatorId);
        }
        foreach (var category in viewer.RegistryCategories)
        {
            access |= filter.Eq(x => x.Category, category)
                | (filter.Eq(x => x.Category, TicketCategory.TechnicalSupport)
                   & filter.Eq(x => x.RegistryCategory, category)
                   & filter.Eq(x => x.RaisedByRegistryStaff, true));
        }
        return access;
    }

    public static bool CanAccess(TicketInfo ticket, TicketViewer viewer) =>
        viewer.CanViewAll || viewer.OwnsTicket(ticket.creatorId) ||
        viewer.RegistryCategories.Any(category => ticket.Category == category ||
            (ticket.Category == TicketCategory.TechnicalSupport && ticket.RegistryCategory == category && ticket.RaisedByRegistryStaff));
}

public sealed class TicketAccessAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            var principal = context.HttpContext.User;
            var viewer = TicketViewer.FromPrincipal(principal);
            var db = context.HttpContext.RequestServices.GetRequiredService<IMongoDatabase>();
            var users = db.GetCollection<AppUser>("appUsers");
            var filter = Builders<AppUser>.Filter.Eq(x => x.Id, viewer.UserId);
            if (ObjectId.TryParse(viewer.UserId, out var objectId))
            {
                filter |= new BsonDocument("_id", objectId);
            }
            var account = await users.Find(filter).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
            if (account == null)
            {
                throw new AuthenticationException("The authenticated account was not found.");
            }
            context.HttpContext.Features.Set(TicketViewer.FromPrincipal(principal, account));
        }
        catch (AuthenticationException)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var executed = await next();
        if (executed.Exception is UnauthorizedAccessException)
        {
            executed.Result = new ForbidResult();
            executed.ExceptionHandled = true;
        }
    }
}
