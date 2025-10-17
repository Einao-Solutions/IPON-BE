using System.ComponentModel;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using IContainer = QuestPDF.Infrastructure.IContainer;

namespace patentdesign
{
    public class AcknowledgementModelDesign(Filling model, string url, List<byte[]> images, string paymentDate) : IDocument
    {
        private Filling model { get; set; } = model;
        private string url { get; set; } = url;

        private string paymentDate { get; set; } = paymentDate;

        
         public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Height(30).AlignRight(). Image("assets/ministry.png").FitArea();
                });
            });
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
        static IContainer SNBlock(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MaxWidth(30)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
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
        static IContainer AttachmentStyle(IContainer container)
        {
            return container
                .ShowOnce()
                .MaxWidth(80)
                // .MinWidth(50)
                // .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }
        void ComposeContent(IContainer container)
        {
             container
                .PaddingVertical(10)
                .Column(column =>
                {
                    column.Item().Height(60).AlignCenter(). Image("assets/ministry.png").FitArea();
                    column.Item().Height(20);
                    column.Item().AlignCenter().Text("DESIGN ACKNOWLEDGEMENT LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                    column.Item().Height(10);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(tablecolumns =>
                        {
                            tablecolumns.RelativeColumn();
                            tablecolumns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Filing Information").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text("Payment date").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(paymentDate ?? "N/A");
                        table.Cell().Element(Block).Text("File Number").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(model.FileId);
                        table.Cell().Element(Block).Text("Payment ID").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(model.ApplicationHistory[0].PaymentId);
                        table.Cell().Element(Block).Text("System ID").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(model.Id);
                    });
                    column.Item().Text("Title of Design")
                        .Style(TextStyle.Default.Bold());
                    column.Item().Text(model.TitleOfDesign).Justify();
                    column.Item().Text("Statement of Novelty")
                        .Style(TextStyle.Default.Bold());
                    column.Item().Text(model.StatementOfNovelty).Justify();
                    column.Item().Text("Design Type").Bold();
                    column.Item().Text(model.DesignType.ToString());
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(defs =>
                        {
                            defs.ConstantColumn(30);
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                        });
                        
                        table.Header(header =>
                        {
                            header.Cell().ColumnSpan(6).Element(HeaderElement).Text("Applicants Information").Style(TextStyle.Default.SemiBold());
                            header.Cell().Element(SNBlock).Text("S/N");
                            header.Cell().Element(Block).Text("Applicant name");
                            header.Cell().Element(Block).Text("Country");
                            header.Cell().Element(Block).Text("Phone number");
                            header.Cell().Element(Block).Text("Email");
                            header.Cell().Element(Block).Text("Address");
                            
                        });
                        foreach (var applicant in model.applicants)
                        {
                            table.Cell().Element(SNBlock).Text((model.applicants.IndexOf(applicant)+1).ToString());
                            table.Cell().Element(Block).Text(applicant.Name);
                            table.Cell().Element(Block).Text(applicant.country);
                            table.Cell().Element(Block).Text(applicant.Phone);
                            table.Cell().Element(Block).Text(applicant.Email);
                            table.Cell().Element(Block).Text(applicant.Address);
                        }
                    });
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(defs =>
                        {
                            defs.ConstantColumn(30);
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                        });
                        
                        table.Header(header =>
                        {
                            header.Cell().ColumnSpan(6).Element(HeaderElement).Text("Design Creators").Style(TextStyle.Default.SemiBold());
                            header.Cell().Element(SNBlock).Text("S/N");
                            header.Cell().Element(Block).Text("Name");
                            header.Cell().Element(Block).Text("Country");
                            header.Cell().Element(Block).Text("Address");
                            header.Cell().Element(Block).Text("Phone Number");
                            header.Cell().Element(Block).Text("Email");
                        });
                        foreach (var applicant in model.DesignCreators)
                        {
                            table.Cell().Element(SNBlock).Text((model.DesignCreators.IndexOf(applicant)+1).ToString());
                            table.Cell().Element(Block).Text(applicant.Name);
                            table.Cell().Element(Block).Text(applicant.country);
                            table.Cell().Element(Block).Text(applicant.Address);
                            table.Cell().Element(Block).Text(applicant.Phone);
                            table.Cell().Element(Block).Text(applicant.Email);
                        }
                    });
                    
                     
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(defs =>
                        {
                            defs.ConstantColumn(30);
                            defs.RelativeColumn();
                            defs.RelativeColumn();
                        });
                        
                        table.Header(header =>
                        {
                            header.Cell().ColumnSpan(3).Element(HeaderElement).Text("Documents Attached").Style(TextStyle.Default.SemiBold());
                            header.Cell().Element(SNBlock).Text("S/N");
                            header.Cell().Element(Block).Text("Document Name");
                            header.Cell().Element(Block).Text("Status");
                        });
                        
                            table.Cell().Element(SNBlock).Text(1.ToString());
                            table.Cell().Element(Block).Text("Priority Document");
                            table.Cell().Element(Block).Row(row=>
                            {
                                if (model.Attachments.Count(x => x.name=="pdoc")==1)
                                {
                                row.AutoItem().PaddingLeft(20).Height(20). Image("assets/checkmark.png").FitArea();
                                row.RelativeItem().PaddingLeft(5).Element(AttachmentStyle).Text("Attached");
                                }
                                else
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/cancel.png").FitArea();
                                    row.RelativeItem().Element(AttachmentStyle).Text("Not Attached");  
                                }
                            });
                            table.Cell().Element(SNBlock).Text(2.ToString());
                            table.Cell().Element(Block).Text("Novelty Statement");
                            table.Cell().Element(Block).Row(row=>
                            {
                                if (model.Attachments.Count(x=>x.name=="nov")==1)
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/checkmark.png").FitArea();
                                    row.RelativeItem().PaddingLeft(5).Element(AttachmentStyle).Text("Attached");
                                }
                                else
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/cancel.png").FitArea();
                                    row.RelativeItem().Element(AttachmentStyle).Text("Not Attached");  
                                }
                            });
                            table.Cell().Element(SNBlock).Text(3.ToString());
                            table.Cell().Element(Block).Text("Design Representations(s)");
                            table.Cell().Element(Block).Row(row=>
                            {
                                if (model.Attachments.Count(x=>x.name=="designs")>=1)
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/checkmark.png").FitArea();
                                    row.RelativeItem().PaddingLeft(5).Element(AttachmentStyle).Text("Attached");
                                }
                                else
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/cancel.png").FitArea();
                                    row.RelativeItem().Element(AttachmentStyle).Text("Not Attached");  
                                }
                            });
                            table.Cell().Element(SNBlock).Text(4.ToString());
                            table.Cell().Element(Block).Text("Power of Attorney");
                            table.Cell().Element(Block).Row(row=>
                            {
                                if (model.Attachments.Count(x=>x.name=="form2")==1)
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/checkmark.png").FitArea();
                                    row.RelativeItem().PaddingLeft(5).Element(AttachmentStyle).Text("Attached");
                                }
                                else
                                {
                                    row.AutoItem().PaddingLeft(20).Height(20). Image("assets/cancel.png").FitArea();
                                    row.RelativeItem().Element(AttachmentStyle).Text("Not Attached");  
                                }
                            });
                            // design drawing
                    });
                    column.Item().Text("Design Representations");
                    
                    foreach (var image in images)
                    {
                        var img = Image.FromBinaryData(image);
                        column.Item().Height(100).AlignCenter().Image(img).FitArea();
                    }
                    column.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION");
                    column.Item().AlignCenter().Text("PATENT AND DESIGN REGISTRY");
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                    // column.Item().AlignCenter().Height(100).Element(GetQrCode);
                    // column.Spacing(15);
                });
        }
        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                container.Image(qrCodeImage).FitArea();
            }
        }
    }
}