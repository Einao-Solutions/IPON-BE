using System;
using System.Collections.Generic;
using System.Linq;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class AvailabilitySearchReceipt(RemitaResponseClass remitaResponse, string rrr, string? searchTitle = null, List<AvailabilitySearchDto>? matches = null, DateTime? searchDate = null) : IDocument
    {
        private RemitaResponseClass remitaResponse { get; set; } = remitaResponse;
        private string rrr { get; set; } = rrr;
        private string? searchTitle { get; set; } = searchTitle;
        private List<AvailabilitySearchDto> matches { get; set; } = matches ?? new List<AvailabilitySearchDto>();
        private DateTime? searchDate { get; set; } = searchDate;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        static IContainer Block(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MinHeight(20)
                .PaddingVertical(3)
                .PaddingLeft(5)
                .AlignLeft();
        }

        static IContainer HeaderElement(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MinHeight(20)
                .AlignMiddle()
                .Background(Colors.Grey.Lighten3)
                .PaddingVertical(1)
                .PaddingLeft(5);
        }

        void ComposeContent(IContainer container)
        {
            container
                .PaddingVertical(5)
                .Column(column =>
                {
                    // Header
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2).FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(10);

                    // Receipt Title
                    column.Item().AlignCenter().Text("AVAILABILITY SEARCH RESULT")
                        .FontColor(Colors.Green.Darken2)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(16)
                        .Bold();
                    column.Item().Height(25);

                    // SEARCH INFORMATION
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("SEARCH INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        var date = searchDate?.ToString("dd/MM/yyyy") ?? "N/A";

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(date).FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(searchTitle ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                    });

                    column.Item().Height(20);

                    // SEARCH RESULTS
                    column.Item().Column(sc =>
                    {
                        sc.Item().Element(HeaderElement).Text("SEARCH RESULTS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        sc.Item().PaddingTop(3).Text($"Searched Title: {searchTitle ?? "N/A"}").FontSize(11).FontFamily(Fonts.TimesNewRoman).Italic();

                        if (matches == null || matches.Count == 0)
                        {
                            sc.Item().PaddingTop(10).AlignCenter().Text("No conflicting marks found").FontSize(12).FontFamily(Fonts.TimesNewRoman).Bold();
                        }
                        else
                        {
                            sc.Item().PaddingTop(8).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1.5f);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1.5f);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderElement).Text("Title").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                                    header.Cell().Element(HeaderElement).Text("Class").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                                    header.Cell().Element(HeaderElement).Text("Applicant").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                                    header.Cell().Element(HeaderElement).Text("Filing Date").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                                    header.Cell().Element(HeaderElement).Text("Similarity").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                                    header.Cell().Element(HeaderElement).Text("Type").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                                });

                                foreach (var m in matches)
                                {
                                    table.Cell().Element(Block).Text(m.TitleOfTradeMark ?? "N/A").FontSize(10).FontFamily(Fonts.TimesNewRoman);
                                    table.Cell().Element(Block).Text(m.TradeMarkClass?.ToString() ?? "N/A").FontSize(10).FontFamily(Fonts.TimesNewRoman);
                                    table.Cell().Element(Block).Text(m.FileApplicant ?? "N/A").FontSize(10).FontFamily(Fonts.TimesNewRoman);
                                    table.Cell().Element(Block).Text(m.FilingDate ?? "N/A").FontSize(10).FontFamily(Fonts.TimesNewRoman);
                                    table.Cell().Element(Block).Text($"{m.Similarity}%").FontSize(10).FontFamily(Fonts.TimesNewRoman);
                                    table.Cell().Element(Block).Text(m.TrademarkType?.ToString() ?? "N/A").FontSize(10).FontFamily(Fonts.TimesNewRoman);
                                }
                            });
                        }
                    });

                    column.Item().Height(20);

                    // Footer
                    column.Item().AlignCenter().Text("PLEASE KEEP THIS RECEIPT FOR FUTURE REFERENCE")
                        .FontColor(Colors.Green.Darken2)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12)
                        .Bold();
                });
        }
    }
}
