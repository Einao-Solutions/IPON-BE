using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using patentdesign.Enums;
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

        private void ComposeHeader(IContainer container)
        {
            var titleStyle = TextStyle.Default.FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text("Certificate Approved").Style(titleStyle);
                    column.Item().Text(text =>
                    {
                        text.Span("Filing Date: ").SemiBold();
                        text.Span(model.DateCreated.ToString());
                    });
                });
                row.ConstantItem(100).Height(75).Image("assets/ministry.png").FitArea();
            });
        }

        private void ComposeContent(IContainer container)
        {
            var title = model.Type == FileTypes.Design ? model.TitleOfDesign :
                model.Type == FileTypes.Patent ? model.TitleOfInvention : model.TitleOfTradeMark;

            container.Layers(layers =>
            {
                var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "design_certificate.png");
                layers.Layer().Image(imagePath).FitArea();

                layers.PrimaryLayer()
                    .PaddingHorizontal(40)
                    .PaddingRight(10)
                    .PaddingVertical(10)
                    .Column(column =>
                    {
                        column.Item().Height(5);
                        column.Item().Height(60).AlignCenter().Image("assets/ministry.png").FitArea();
                        column.Item().Height(40);
                        column.Item().AlignCenter().Text("Certificate of Registration Design")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                        column.Item().Height(20);
                        column.Item().Height(5);
                        column.Item().AlignRight().Text(model.FileId).FontSize(12);
                        column.Item().AlignRight().Text(model.Id).FontSize(12);
                        column.Item().Height(20);
                        column.Item().PaddingLeft(70).Text(ConstantValues.DesignCertificate).FontSize(12).Justify();
                        column.Item().Height(20);

                        var applicantName = model.applicants.Count > 1
                            ? model.applicants[0].Name + " et al."
                            : model.applicants[0].Name;
                        var applicantAddress = model.applicants[0].Address;

                        column.Item().PaddingLeft(70).Text(applicantName).FontSize(12);
                        column.Item().PaddingLeft(70).Text(applicantAddress).FontSize(12);
                        column.Item().Height(20);
                        column.Item().PaddingLeft(70).Text($"C/O {model.Correspondence?.name}").FontSize(12);
                        column.Item().PaddingLeft(70).Text(model.Correspondence?.address ?? string.Empty).FontSize(12);
                        column.Item().PaddingLeft(70).Height(20);
                        column.Item().PaddingLeft(70).Text($"In respect 1. {model.TitleOfDesign}");
                        column.Item().PaddingLeft(70).Height(10);

                        var searchDate = model.ApplicationHistory?[0].StatusHistory
                            ?.FirstOrDefault(x => x.afterStatus == ApplicationStatuses.AwaitingSearch)?.Date;
                        var activeDate = model.ApplicationHistory?[0].StatusHistory
                            ?.FirstOrDefault(x => x.afterStatus == ApplicationStatuses.Active)?.Date;

                        var asOfDate = searchDate?.ToString("D") ?? activeDate?.ToString("D") ?? model.DateCreated.ToString("D");
                        var datedDate = activeDate?.ToString("D") ?? model.DateCreated.ToString("D");

                        column.Item().PaddingLeft(70)
                            .Text($"As of the {asOfDate}")
                            .FontSize(12);
                        column.Item().PaddingLeft(70)
                            .Text($"Dated this {datedDate}")
                            .FontSize(12);
                        column.Item().Height(130);
                        column.Item().Height(50).AlignCenter().Image("assets/signature.jpeg").FitArea();
                        column.Item().AlignCenter().Text("Jane Igwe").Bold();
                        column.Item().AlignCenter().Text("Registrar Patents and Designs").Bold();
                        column.Item().Height(30);
                        column.Item().PaddingLeft(70)
                            .Text($"Copyright in this Design will expire on {expiryDate} and may on application made in the prescribe manner, be extended for two further periods of five years each")
                            .FontSize(12).Justify();
                    });
            });
        }
    }
}
