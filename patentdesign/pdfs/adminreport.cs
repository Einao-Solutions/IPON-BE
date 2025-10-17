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
//     public class AdminReport(List<TradeMark> result, StatsRequest dates) : IDocument
//     {
//         private int NewDesignTextileApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType == "Fresh" &&
//                        x.Type == TradeMarkCategories.Design && x.DesignType is DesignTypes.Textile;
//             });
//         }
//         private int NewDesignNonTextileApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType == "Fresh" &&
//                        x.Type == TradeMarkCategories.Design && x.DesignType is DesignTypes.Non_textile;
//             });
//         }
//
//         private int NewPatentConventionalApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType == "Fresh" &&
//                        x.Type == TradeMarkCategories.Patent && x.PatentType is PatentTypes.Conventional;
//             });
//         }
//         
//         private int NewPatentNonConventionalApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType == "Fresh" &&
//                        x.Type == TradeMarkCategories.Patent && x.PatentType is PatentTypes.Non_Conventional;
//             });
//         }
//
//         private int PatentNonConvLicenseRenewalApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType != "Fresh" &&
//                        x.Type == TradeMarkCategories.Patent && x.PatentType is PatentTypes.Non_Conventional;
//             });
//         }
//         
//         private int PatentConvLicenseRenewalApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType != "Fresh" &&
//                        x.Type == TradeMarkCategories.Patent && x.PatentType is PatentTypes.Conventional;
//             });
//         }
//         
//         private int DesignTextileLicenseRenewalApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType != "Fresh" &&
//                        x.Type == TradeMarkCategories.Design && x.DesignType is DesignTypes.Textile;
//             });
//         }
//         
//          
//         private int DesignNonTextileLicenseRenewalApplications()
//         {
//             return result.Count(x =>
//             {
//                 var license = x.LicenseHistories.FirstOrDefault(y => y.id == x.LicenseID);
//                 var date = license.ApplicationDate;
//                 return date.CompareTo(dates.startDate) >= 0 &&
//                        date.CompareTo(dates.endDate) <= 0 && license.LicenseType != "Fresh" &&
//                        x.Type == TradeMarkCategories.Design && x.DesignType is DesignTypes.Non_textile;
//             });
//         }
//         
//         private int DesignDataUpdateApplications()
//         {
//             return result.Count(x =>
//             {
//                 var revision = x.Revisions.Where(y =>
//                     y.TransactionId != "-" && y.DateTime.CompareTo(dates.startDate) >= 0 &&
//                     y.DateTime.CompareTo(dates.endDate) <= 0);
//                 return x.Type is TradeMarkCategories.Design && revision.Any();
//             });
//         }
//         
//         
//         private int PatentDataUpdateApplications()
//         {
//             return result.Count(x =>
//             {
//                 var revision = x.Revisions.Where(y =>
//                     y.TransactionId != "-" && y.DateTime.CompareTo(dates.startDate) >= 0 &&
//                     y.DateTime.CompareTo(dates.endDate) <= 0);
//                 return x.Type is TradeMarkCategories.Patent && revision.Any();
//             });
//         }
//         
//         public void Compose(IDocumentContainer container)
//         {
//             container.Page(page =>
//             {
//                 page.Margin(50);
//                 page.Content().Element(ComposeContent);
//                 page.Footer().Height(50).Background(Colors.Green.Lighten1);
//             });
//         }
//
//         private void ComposeContent(IContainer container)
//         { 
//             container
//                 .PaddingVertical(40)
//                 .Column(column =>
//                 {
//                     column.Item().Height(60).AlignCenter(). Image("assets/ministry.png").FitArea();
//                     column.Item().Height(20);
//                     column.Item().AlignCenter().Text("REPORT OF PATENTS AND DESIGNS").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
//                     column.Item().AlignCenter().Text($"BETWEEN {dates.startDate.Date} and {dates.endDate.Date}").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
//                     column.Item().Height(30);
//                     column.Item().Row(row =>
//                     {
//                         row.Spacing(10);
//                         row.RelativeItem().Column(c =>
//                         {
//                             c.Item().PaddingBottom(10).Text($"New Patent Applications: {NewPatentConventionalApplications() + NewPatentNonConventionalApplications()}");
//                             c.Item().Border(1).ExtendHorizontal().Height(300).SkiaSharpCanvas((canvas, size) =>
//                             {
//                                 List<ChartEntry> entries = [new ChartEntry(NewPatentConventionalApplications())
//                                     {
//                                         ValueLabel = $"{NewPatentConventionalApplications()}",
//                                         Label = "Conventional",
//                                         Color = SKColor.Parse("#77d065")
//
//                                     },
//                                     new ChartEntry(NewPatentNonConventionalApplications())
//                                     {
//                                         ValueLabel = $"{NewPatentNonConventionalApplications()}",
//                                         Label = "Non Conventional",
//                                         Color = SKColor.Parse("#b455b6")
//                                     },];
//                                 var chart = new BarChart
//                                 {
//                                     Entries = entries,
//                                     LabelTextSize = 14,
//                                     BarAreaAlpha = 20,
//                                     LabelOrientation = Orientation.Vertical,
//                                     ValueLabelOrientation = Orientation.Horizontal,
//                 
//                                     IsAnimated = false,
//                                 };
//                                 chart.DrawContent(canvas, (int)size.Width, (int)size.Height);
//                             });
//                         });
//                         row.RelativeItem().Column(c =>
//                         {
//                             c.Item().PaddingBottom(10).Text($"New Design Applications: {NewDesignTextileApplications() + NewDesignNonTextileApplications()}");
//                             c.Item().Border(1).ExtendHorizontal().Height(300).SkiaSharpCanvas((canvas, size) =>
//                             {
//                                 List<ChartEntry> entries = [new ChartEntry(NewDesignTextileApplications())
//                                     {
//                                         ValueLabel = $"{NewDesignTextileApplications()}",
//                                         Label = "Textile",
//                                         Color = SKColor.Parse("#77d065")
//
//                                     },
//                                     new ChartEntry(NewDesignNonTextileApplications())
//                                     {
//                                         ValueLabel = $"{NewDesignNonTextileApplications()}",
//                                         Label = "Non Textile",
//                                         Color = SKColor.Parse("#b455b6")
//                                     },];
//                                 var chart = new BarChart
//                                 {
//                                     Entries = entries,
//                                     LabelTextSize = 14,
//                                     LabelOrientation = Orientation.Vertical,
//
//                                     ValueLabelOrientation = Orientation.Horizontal,
//                 
//                                     IsAnimated = false,
//                                 };
//                                 chart.DrawContent(canvas, (int)size.Width, (int)size.Height);
//                             });
//                         });
//                     });
//                     column.Item().Height(20);
//                     column.Item().Row(row =>
//                     {
//                         row.Spacing(10);
//                         row.RelativeItem().Column(c =>
//                         {
//                             c.Item().PaddingBottom(10).Text($"Patent Renewal: {PatentConvLicenseRenewalApplications() + PatentNonConvLicenseRenewalApplications()}");
//                             c.Item().Border(1).ExtendHorizontal().Height(300).SkiaSharpCanvas((canvas, size) =>
//                             {
//                                 List<ChartEntry> entries = [new ChartEntry(PatentConvLicenseRenewalApplications())
//                                     {
//                                         ValueLabel = $"{PatentConvLicenseRenewalApplications()}",
//                                         Label = "Conventional",
//                                         Color = SKColor.Parse("#77d065")
//
//                                     },
//                                     new ChartEntry(PatentNonConvLicenseRenewalApplications())
//                                     {
//                                         ValueLabel = $"{PatentNonConvLicenseRenewalApplications()}",
//                                         Label = "Non Conventional",
//                                         Color = SKColor.Parse("#b455b6")
//                                     },];
//                                 var chart = new BarChart
//                                 {
//                                     Entries = entries,
//                                     LabelTextSize = 14,
//
//                                     LabelOrientation = Orientation.Vertical,
//
//                                     ValueLabelOrientation = Orientation.Horizontal,
//                                     BarAreaAlpha = 20,
//
//                                     IsAnimated = false,
//                                 };
//                                 chart.DrawContent(canvas, (int)size.Width, (int)size.Height);
//                             });
//                         });
//                         row.RelativeItem().Column(c =>
//                         {
//                             c.Item().PaddingBottom(10).Text($"Design Renewal: {DesignTextileLicenseRenewalApplications() + DesignNonTextileLicenseRenewalApplications()}");
//                             c.Item().Border(1).ExtendHorizontal().Height(300).SkiaSharpCanvas((canvas, size) =>
//                             {
//                                 List<ChartEntry> entries = [new ChartEntry(DesignTextileLicenseRenewalApplications())
//                                     {
//                                         ValueLabel = $"{DesignTextileLicenseRenewalApplications()}",
//                                         Label = "Textile",
//                                         Color = SKColor.Parse("#77d065")
//
//                                     },
//                                     new ChartEntry(DesignNonTextileLicenseRenewalApplications())
//                                     {
//                                         ValueLabel = $"{DesignNonTextileLicenseRenewalApplications()}",
//                                         Label = "Non Textile",
//                                         Color = SKColor.Parse("#b455b6")
//                                     },];
//                                 var chart = new BarChart
//                                 {
//                                     Entries = entries,
//                                     LabelTextSize = 14,
//                                     BarAreaAlpha = 20,
//
//                                     LabelOrientation = Orientation.Vertical,
//
//                                     ValueLabelOrientation = Orientation.Horizontal,
//                 
//                                     IsAnimated = false,
//                                 };
//                                 chart.DrawContent(canvas, (int)size.Width, (int)size.Height);
//                             });
//                         });
//                     });
//                     column.Item().Height(20);
//                     column.Item().PaddingBottom(10).Text($"Data Update: {PatentDataUpdateApplications() + DesignDataUpdateApplications()}");
//                     column.Item().Border(1).ExtendHorizontal().Height(300).SkiaSharpCanvas((canvas, size) =>
//                     {
//                         List<ChartEntry> entries = [new ChartEntry(PatentDataUpdateApplications())
//                             {
//                                 ValueLabel = $"{PatentDataUpdateApplications()}",
//                                 Label = "Patents",
//                                 Color = SKColor.Parse("#77d065")
//
//                             },
//                             new ChartEntry(DesignDataUpdateApplications())
//                             {
//                                 ValueLabel = $"{DesignDataUpdateApplications()}",
//                                 Label = "Designs",
//                                 Color = SKColor.Parse("#b455b6")
//                             },];
//                         var chart = new BarChart
//                         {
//                             Entries = entries,
//                             ValueLabelOrientation = Orientation.Horizontal,
//                             LabelOrientation = Orientation.Vertical,
//
//                             LabelTextSize = 14,
//                             BarAreaAlpha = 20,
//
//                             IsAnimated = false,
//                         };
//                         chart.DrawContent(canvas, (int)size.Width, (int)size.Height);
//                     });
//                     
//                 });
//         }
//     }
// }