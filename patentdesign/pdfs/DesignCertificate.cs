using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class DesignCertificate(Filling model, string expiryDate) : IDocument
    {
        private Filling model { get; set; } = model;
        private string expiryDate { get; set; } = expiryDate;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(5);
                page.Content().Element(ComposeContent);
            });
        }

        void ComposeHeader(IContainer container)
        {
            var titleStyle = TextStyle.Default.FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text($"Certificate Approved").Style(titleStyle);
                    column.Item().Text(text =>
                    {
                            text.Span("Filing Date: ").SemiBold();
                            text.Span(model.DateCreated.ToString());
                    });
                    row.ConstantItem(100).Height(75).Image("assets/ministry.png").FitArea();
                });
            });
        }
        
        void ComposeContent(IContainer container)
        {
            var title = model.Type==FileTypes.Design? model.TitleOfDesign: model.Type==FileTypes.Patent? model.TitleOfInvention:model.TitleOfTradeMark;
            
            container.Layers(layers =>
            {
                var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "design_certificate.png");
                layers.Layer()
                    .Image(imagePath).FitArea();
                layers
                    .PrimaryLayer()
                    .PaddingHorizontal(40)
                    .PaddingRight(10)
                    .PaddingVertical(10)
                    .Column(column =>
                    {
                        column.Item().Height(5);
                        column.Item().Height(60).AlignCenter().Image("assets/ministry.png").FitArea();
                        column.Item().Height(40);
                        // column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                        // column.Item().AlignCenter().Text("Patents and Designs Act.").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                        // column.Item().AlignCenter().Text("(Cap 344 Laws of the Federation of Nigeria 1990)").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                        column.Item().AlignCenter().Text($"Certificate of Registration Design").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                        column.Item().Height(20);
                        column.Item().Height(5);
                        column.Item().AlignRight().Text(model.FileId).FontSize(12);
                        column.Item().AlignRight().Text(model.Id).FontSize(12);
                        column.Item().Height(20);
                        column.Item().PaddingLeft(70).Text(ConstantValues.DesignCertificate).FontSize(12).Justify();
                        column.Item().Height(20);
                        var applicantName = model.applicants.Count > 1 ? model.applicants[0].Name + "et al.":model.applicants[0].Name ;
                        var applicantAddress = model.applicants[0].Address;
                        column.Item().PaddingLeft(70).Text(applicantName).FontSize(12);
                        column.Item().PaddingLeft(70).Text(applicantAddress).FontSize(12);
                        column.Item().Height(20);
                        column.Item().PaddingLeft(70).Text($"C/O {model.Correspondence?.name}").FontSize(12);
                        column.Item().PaddingLeft(70).Text($"C/O {model.Correspondence?.address}").FontSize(12);
                        column.Item().PaddingLeft(70).Height(20);
                        column.Item().PaddingLeft(70).Text($"In respect 1. {model.TitleOfDesign}");
                        column.Item().PaddingLeft(70).Height(10);
                        column.Item().PaddingLeft(70).Text($"As of the {model.ApplicationHistory[0].StatusHistory.FirstOrDefault(x=>x.afterStatus==ApplicationStatuses.AwaitingSearch)?.Date.ToString("D") ?? model.ApplicationHistory[0].StatusHistory.FirstOrDefault(x=>x.afterStatus==ApplicationStatuses.Active)?.Date.ToString("D") }").FontSize(12);
                        column.Item().PaddingLeft(70).Text($"Dated this {model.ApplicationHistory[0].StatusHistory.FirstOrDefault(x=>x.afterStatus==ApplicationStatuses.Active)?.Date.ToString("D")}").FontSize(12);
                        column.Item().Height(130);
                        column.Item().Height(50).AlignCenter().Image("assets/signature.jpeg").FitArea();
                        column.Item().AlignCenter().Text("Jane Igwe").Bold();
                        column.Item().AlignCenter().Text("Registrar Patents and Designs").Bold();
                        column.Item().Height(30);
                        column.Item().PaddingLeft(70).Text($"Copyright in this Design will expire on {expiryDate} and may on application made in the prescribe manner, be extended for two further periods of five years each").FontSize(12).Justify();
                    });
            });
        }
    }
}