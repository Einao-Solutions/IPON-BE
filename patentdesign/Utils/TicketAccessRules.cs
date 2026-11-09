using System.Security.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

public sealed record TicketViewer
{
    public string UserId { get; private init; } = string.Empty;
    public string DisplayName { get; private init; } = string.Empty;
    public bool CanViewAll { get; private init; }
    public IReadOnlyList<TicketCategory> RegistryCategories { get; private init; } = Array.Empty<TicketCategory>();

    private TicketViewer() { }

    public static TicketViewer FromPrincipal(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            throw new AuthenticationException("An authenticated user identifier is required.");
        }

        var categories = new List<TicketCategory>();
        if (principal.IsInRole(nameof(Roles.TrademarkSupport))) categories.Add(TicketCategory.TrademarkRegistry);
        if (principal.IsInRole(nameof(Roles.PatentDesignSupport))) categories.Add(TicketCategory.PatentDesignRegistry);
        return new TicketViewer
        {
            UserId = userId,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue(ClaimTypes.Email) ?? userId,
            CanViewAll = principal.IsInRole(nameof(Roles.Tech)) || principal.IsInRole(nameof(Roles.SuperAdmin)),
            RegistryCategories = categories.AsReadOnly()
        };
    }
}

public static class TicketAccessRules
{
    public static FilterDefinition<TicketInfo> Filter(TicketViewer? viewer)
    {
        var filter = Builders<TicketInfo>.Filter;
        if (viewer == null || viewer.CanViewAll) return filter.Empty;

        var access = filter.Eq(x => x.creatorId, viewer.UserId);
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
        viewer.CanViewAll || ticket.creatorId == viewer.UserId ||
        viewer.RegistryCategories.Any(category => ticket.Category == category ||
            (ticket.Category == TicketCategory.TechnicalSupport && ticket.RegistryCategory == category && ticket.RaisedByRegistryStaff));
}

public sealed class TicketAccessAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            _ = TicketViewer.FromPrincipal(context.HttpContext.User);
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
