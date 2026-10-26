using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Controllers;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Utils;
using Xunit;

namespace patentdesign.Tests.Services
{
    public class AvailabilitySearchAdminLookupTests
    {
        private static ApplicationInfo NewAvs(string id = "abc123def456ghi")
        {
            var app = new ApplicationInfo
            {
                id = id,
                ApplicationType = FormApplicationTypes.AvailabilitySearch,
                CurrentStatus = ApplicationStatuses.AwaitingPayment,
                ApplicationDate = new DateTime(2024, 1, 1),
                PaymentId = "RRR1",
                Title = "ACME"
            };
            return app;
        }

        private static ClaimsPrincipal Principal(string? role)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u1") };
            if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        [Fact]
        public void Lookup_ByReference_ReturnsRecordWithHistoryFields()
        {
            var app = NewAvs();
            var user = new AppUser { Id = "u1", OtherApplications = new List<ApplicationInfo> { app } };
            var reference = AvailabilitySearchRules.BuildReferenceNumber(app.id)!;

            var match = AvailabilitySearchRules.FindByReference(new[] { user }, reference.ToLowerInvariant());
            Assert.NotNull(match);

            var dto = AvailabilitySearchRules.ToFileUpdateDto(match!.Value.Application);
            Assert.Equal(reference, dto.ReferenceNumber);
            var entry = Assert.Single(dto.ApplicationHistory);
            Assert.Equal(app.id, entry.id);
            Assert.Equal("RRR1", entry.PaymentId);
            Assert.Equal(ApplicationStatuses.AwaitingPayment, entry.CurrentStatus);
        }

        [Fact]
        public void Lookup_UnknownOrInvalidReference_ReturnsNull()
        {
            var user = new AppUser { Id = "u1", OtherApplications = new List<ApplicationInfo> { NewAvs() } };
            Assert.Null(AvailabilitySearchRules.FindByReference(new[] { user }, "AVS-NOPE000000"));
            Assert.Null(AvailabilitySearchRules.FindByReference(new[] { user }, "TM/2024/1"));
            Assert.Null(AvailabilitySearchRules.NormalizeReference(null));
        }

        [Fact]
        public void ApplyUpdate_PersistsEditableFields()
        {
            var app = NewAvs();
            var changed = AvailabilitySearchRules.ApplyUpdate(app, new DateTime(2025, 5, 5),
                ApplicationStatuses.AutoApproved, "RRR2", "CERT1");

            Assert.True(changed);
            Assert.Equal(new DateTime(2025, 5, 5), app.ApplicationDate);
            Assert.Equal(ApplicationStatuses.AutoApproved, app.CurrentStatus);
            Assert.Equal("RRR2", app.PaymentId);
            Assert.Equal("CERT1", app.CertificatePaymentId);
            Assert.False(AvailabilitySearchRules.ApplyUpdate(app, null, null, null, null));
        }

        [Fact]
        public void SuperAdminCheck_AllowsOnlySuperAdmin()
        {
            Assert.True(AvailabilitySearchRules.IsSuperAdmin(Principal("SuperAdmin")));
            Assert.False(AvailabilitySearchRules.IsSuperAdmin(Principal("User")));
            Assert.False(AvailabilitySearchRules.IsSuperAdmin(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        [Fact]
        public async Task GetAllFileDetails_ReferenceLookup_RejectsUnauthorizedCallers()
        {
            var controller = new FilesController(null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
                }
            };
            Assert.IsType<UnauthorizedResult>(await controller.GetAllFileDetails(null, "AVS-ABC123"));

            controller.ControllerContext.HttpContext!.User = Principal("User");
            Assert.IsType<ForbidResult>(await controller.GetAllFileDetails(null, "AVS-ABC123"));

            controller.ControllerContext.HttpContext.User = Principal("SuperAdmin");
            Assert.IsType<BadRequestObjectResult>(await controller.GetAllFileDetails(null, "bad ref"));
        }

        [Fact]
        public void UpdateEndpoint_RequiresSuperAdminRole()
        {
            var method = typeof(AdminController).GetMethod(nameof(AdminController.UpdateApplicationHistory))!;
            var attr = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("SuperAdmin", attr!.Roles);
        }
    }
}
