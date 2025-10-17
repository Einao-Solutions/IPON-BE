// your renewal applicaiton for 24/25 has been accepted for
// file id
// file number
// title of application
// date
// examiner name

using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;


public class DesignRenewalCertificate(Filling fileData, string applicationId):IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(1);
            page.Content().Element(ComposeContent);
        });
    }
    static IContainer HeaderElement(IContainer container)
    {
        return container
            .Border(1)
            .Background(Colors.Grey.Lighten3)
            .ShowOnce()
            .MinWidth(50)
            .MinHeight(30)
            .AlignCenter()
            .AlignMiddle();
    }
        
    static IContainer Block(IContainer container)
    {
        return container
            .Border(1)
            .ShowOnce()
            .MinWidth(50)
            .MinHeight(30)
            .AlignCenter()
            .AlignMiddle();
    }

    private void ComposeContent(IContainer container)
    {
        var applicant = fileData.applicants.Count > 1
            ? fileData.applicants[0].Name + " .et al"
            : fileData.applicants[0].Name;
        var application=fileData.ApplicationHistory.FirstOrDefault(x => x.id == applicationId);
        var date=application.ApplicationDate;
        var numberOfRenewals = fileData.ApplicationHistory.Where(d=>d.ApplicationType==FormApplicationTypes.LicenseRenewal).OrderBy(d => d.ApplicationDate).Select(f => f.id).ToList()
            .IndexOf(applicationId);
        var expiry = application.ExpiryDate;
        var approvalDate = application.StatusHistory.FirstOrDefault(f =>
            f.afterStatus == ApplicationStatuses.Active || f.afterStatus == ApplicationStatuses.Approved).Date;
        var approver = application.StatusHistory.FirstOrDefault(f =>
            f.afterStatus == ApplicationStatuses.Active || f.afterStatus == ApplicationStatuses.Approved).User;
        container.Layers(layers =>
        {
            layers.Layer()
                .Image("assets/design_renewal.png").FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(40)
                .PaddingRight(50)
                .PaddingLeft(50)
                .PaddingVertical(10).PaddingVertical(10)
                .Column(column =>
                {
                    column.Item().Height(220);
                    column.Item().Text($"THE {ToOrdinalString(numberOfRenewals+1).ToUpper()} PERIOD OF FIVE YEARS").AlignCenter();
                    column.Item().Height(10);
                    column.Item().Text("This is to certify that").Italic().AlignCenter();
                    column.Item().Text(applicant).SemiBold().AlignCenter();
                    column.Item().Height(30);
                    column.Item().Text($"Did this {date.ToString("D")} ,  make application and pay the prescribed fee for the extention of design right in the registered design no.{fileData.FileId}.\n").Justify();
                    column.Item().Height(10);
                    column.Item().Text($"Titled {fileData.TitleOfDesign} and the design is hereby extended for a {ToOrdinalString(numberOfRenewals+1)} period of five (5) years  until the {DateTime.Parse(expiry.ToString()).ToString("D") }.\n").Justify();
                    column.Item().Height(50);
                    column.Item().Text($"Dated this: {approvalDate.ToString("D")}").AlignCenter();
                });
            layers.Layer().Column(d =>
            {
                d.Item().Height(670);
                d.Item().PaddingRight(210).Text(approver).AlignEnd();
            });
        });
    }
    
    public static string ToOrdinalString(int number)
    {
        switch (number)
        {
            case 1:
                return "1st";
            case 2:
                return "2nd";
            case 3:
                return "3rd";
            default:
                return $"{number}th";
        }
    }


}