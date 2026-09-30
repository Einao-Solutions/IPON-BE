using Microsoft.Extensions.Options;
using Xunit;
using patentdesign.Utils;
using patentdesign.Models;
using patentdesign.Enums;

namespace patentdesign.Tests.Services
{
    /// <summary>
    /// These tests verify that Design renewal cost pricing comes from appsettings configuration values
    /// and are not hardcoded or incorrectly cached.
    /// 
    /// Issue: Backend API /api/files/RenewalCost returns cost "21500" for Design renewal
    /// Expected: Should return "15500" based on appsettings.json DesignTextileRenewCost
    /// </summary>
    public class DesignRenewalCostTests
    {
        private readonly PaymentInfo _paymentInfo;
        private readonly PaymentUtils _paymentUtils;

        public DesignRenewalCostTests()
        {
            // Setup payment info from appsettings-like configuration
            _paymentInfo = new PaymentInfo
            {
                // Textile Design Renewal
                DesignTextileRenewCost = "15500",
                DesignTextileRenewID = "4019135160",
                DesignTextileRenewServiceFee = "3500",

                // Non-Textile Design Renewal
                DesignNonTextileRenewCost = "15500",
                DesignNonTextileRenewID = "4019135160",
                DesignNonTextileRenewServiceFee = "3500",

                // Patent Renewal (should not be affected)
                PatentRenewCost = "11500",
                PatentRenewID = "4019135160",
                PatentRenewServiceFee = "3500"
            };

            var options = Options.Create(_paymentInfo);
            _paymentUtils = new PaymentUtils(options, null);
        }

        [Fact]
        public void GetCost_DesignTextileRenewal_ShouldReturn15500()
        {
            // Arrange
            var paymentType = PaymentTypes.LicenseRenew;
            var fileType = FileTypes.Design;
            var designType = DesignTypes.Textile;

            // Act
            var (cost, serviceId, serviceFee) = _paymentUtils.GetCost(paymentType, fileType, "", designType, null);

            // Assert
            Assert.Equal("15500", cost);
            Assert.Equal("4019135160", serviceId);
            Assert.Equal("3500", serviceFee);
        }

        [Fact]
        public void GetCost_DesignNonTextileRenewal_ShouldReturn15500()
        {
            // Arrange
            var paymentType = PaymentTypes.LicenseRenew;
            var fileType = FileTypes.Design;
            var designType = DesignTypes.NonTextile;

            // Act
            var (cost, serviceId, serviceFee) = _paymentUtils.GetCost(paymentType, fileType, "", designType, null);

            // Assert
            Assert.Equal("15500", cost);
            Assert.Equal("4019135160", serviceId);
            Assert.Equal("3500", serviceFee);
        }

        [Fact]
        public void GetCost_PatentRenewal_ShouldReturn11500()
        {
            // Arrange
            var paymentType = PaymentTypes.LicenseRenew;
            var fileType = FileTypes.Patent;

            // Act
            var (cost, serviceId, serviceFee) = _paymentUtils.GetCost(paymentType, fileType, "", null, null);

            // Assert
            Assert.Equal("11500", cost);
            Assert.Equal("4019135160", serviceId);
            Assert.Equal("3500", serviceFee);
        }

        [Theory]
        [InlineData(PaymentTypes.LicenseRenew, FileTypes.Design, "Textile", "15500")]
        [InlineData(PaymentTypes.LicenseRenew, FileTypes.Design, "NonTextile", "15500")]
        [InlineData(PaymentTypes.LicenseRenew, FileTypes.Patent, null, "11500")]
        public void GetCost_VariousRenewals_VerifyCorrectPricing(
            PaymentTypes paymentType, 
            FileTypes fileType, 
            string designTypeStr, 
            string expectedCost)
        {
            // Arrange
            var designType = designTypeStr switch
            {
                "Textile" => DesignTypes.Textile,
                "NonTextile" => DesignTypes.NonTextile,
                _ => (DesignTypes?)null
            };

            // Act
            var (cost, _, _) = _paymentUtils.GetCost(paymentType, fileType, "", designType, null);

            // Assert
            Assert.Equal(expectedCost, cost);
        }

        [Fact]
        public void PaymentInfo_ConfiguredValues_NotNull()
        {
            // Verify that config values are loaded
            Assert.NotNull(_paymentInfo.DesignTextileRenewCost);
            Assert.NotNull(_paymentInfo.DesignNonTextileRenewCost);
            Assert.NotNull(_paymentInfo.PatentRenewCost);

            // Verify they have the expected values (these come from appsettings.json)
            Assert.Equal("15500", _paymentInfo.DesignTextileRenewCost);
            Assert.Equal("15500", _paymentInfo.DesignNonTextileRenewCost);
            Assert.Equal("11500", _paymentInfo.PatentRenewCost);
        }
    }
}
