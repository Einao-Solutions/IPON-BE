// using System;
// using System.Collections.Generic;
// using System.Linq;
// using Microcharts;
// using QuestPDF.Fluent;
// using QuestPDF.Helpers;
// using QuestPDF.Infrastructure;
// using SkiaSharp;
//
// namespace Tfunctions.pdfs
// {
//     public class GenerateTicketPdf(TicketStats stats, DateTime startDate, DateTime endDate) : IDocument
//     {
//         public void Compose(IDocumentContainer container)
//         {
//             container.Page(page =>
//             {
//                 page.Margin(30);
//                 page.Content().Element(ComposeContent);
//                 page.Footer().Row(row =>
//                 {
//                     row.RelativeItem().Height(30).AlignLeft(). Image("assets/ministry.png").FitArea();
//                     row.RelativeItem().Height(30).AlignRight(). Image("assets/eina.png").FitArea();
//                 });
//             });
//         }
//         static IContainer Block(IContainer container)
//         {
//             return container
//                 .Border(1)
//                 .ShowOnce()
//                 .MinWidth(50)
//                 .MinHeight(30)
//                 .AlignCenter()
//                 .AlignMiddle();
//         }  
//         static IContainer HeaderElement(IContainer container)
//         {
//             return container
//                 .Border(1)
//                 .Background(Colors.Grey.Lighten3)
//                 .ShowOnce()
//                 .MinWidth(50)
//                 .MinHeight(30)
//                 .AlignCenter()
//                 .AlignMiddle();
//         }
//
//         private void ComposeContent(IContainer container)
//         {
//             container
//                 .PaddingVertical(10)
//                 .Column(column =>
//                 {
//                     column.Item().Height(20);
//                     column.Item().AlignCenter().Text("CUSTOMER SUPPORT SUMMARY FOR").FontFamily(Fonts.TimesNewRoman)
//                         .FontSize(18).Bold();
//                     column.Item().Height(10);
//                     column.Item().AlignCenter().Text($"{startDate.Date} - {endDate.Date}");
//                     column.Item().Height(10);
//                     column.Item().Table(table =>
//                     {
//                         table.ColumnsDefinition(tablecolumns =>
//                         {
//                             tablecolumns.RelativeColumn();
//                             tablecolumns.RelativeColumn();
//                         });
//
//                         table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Tickets Information")
//                             .Style(TextStyle.Default.SemiBold());
//                         table.Cell().Element(Block).Text("Total Tickets created").Style(TextStyle.Default.SemiBold());
//                         table.Cell().Element(Block)
//                             .Text((stats.closed + stats.awaitingAgent + stats.awaitingStaff).ToString());
//                         table.Cell().Element(Block).Text("Tickets resolved").Style(TextStyle.Default.SemiBold());
//                         table.Cell().Element(Block).Text(stats.closed.ToString());
//                         table.Cell().Element(Block).Text("Tickets waiting for staff response")
//                             .Style(TextStyle.Default.SemiBold());
//                         table.Cell().Element(Block).Text(stats.awaitingStaff.ToString());
//                         table.Cell().Element(Block).Text("Tickets waiting for agent response")
//                             .Style(TextStyle.Default.SemiBold());
//                         table.Cell().Element(Block).Text(stats.awaitingAgent.ToString());
//                     });
//                     column.Item().AlignCenter().Text("TICKETS RESOLVED BREAKDOWN").FontFamily(Fonts.TimesNewRoman)
//                         .FontSize(18).Bold();
//                     List<ChartEntry> entries = [];
//                     stats.StaffClosures.ToList().ForEach(x =>
//                     {
//                         entries.Add(new ChartEntry(x.Value)
//                         {
//                             Label = x.Key,
//                             ValueLabel = x.Value.ToString(),
//                             Color = SKColor.Parse("#2c3e50")
//                         });
//                     });
//
//                     column
//                         .Item()
//                         .Border(1)
//                         .ExtendHorizontal()
//                         .Height(300)
//                         .SkiaSharpCanvas((canvas, size) =>
//                         {
//                             var chart = new BarChart
//                             {
//                                 Entries = entries,
//
//                                 LabelOrientation = Orientation.Vertical,
//                                 ValueLabelOrientation = Orientation.Horizontal,
//
//                                 IsAnimated = false,
//                             };
//
//                             chart.DrawContent(canvas, (int)size.Width, (int)size.Height);
//                         });
//                 });
//         }
//     }
// }