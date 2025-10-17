using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class AcceptanceModelDesign(
        Filling model,
        string url,
        byte[] signatureUrl,
        List<byte[]> images,
        string examinerName,
        DateTime? approvalDate=null) : IDocument
    {
        private Filling model { get; set; } = model;
        private string url { get; set; } = url;
        
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
                    column.Item().AlignCenter().Text("DESIGN ACCEPTANCE LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                    column.Item().Height(10);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(tablecolumns =>
                        {
                            tablecolumns.RelativeColumn();
                            tablecolumns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Filing Information").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text("Filing date").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(model.DateCreated.ToString());
                        table.Cell().Element(Block).Text("File Number").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(model.FileId);
                        table.Cell().Element(Block).Text("System ID").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(model.Id);
                    });
                    column.Item().Text("Title of Design")
                        .Style(TextStyle.Default.Bold());
                    column.Item().Text(model.TitleOfDesign).Justify();
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
                    column.Item().Text("Design Representations");

                    column.Item().Row(row =>
                    {
                        foreach (var image in images)
                        {
                            var img = Image.FromBinaryData(image);
                            row.RelativeItem().Height(100).Image(img).FitArea();
                            // column.Item().Height(100).AlignCenter().Image(img).FitArea();
                        }
                    });
                    column.Item().AlignCenter().Text("YOUR APPLICATION HAS BEEN ACCEPTED AND CERTIFICATE IS BEING PROCESSED");
                    column.Item().AlignCenter().Text("PATENT AND DESIGN REGISTRY");
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                    // var imgSig = Image.FromBinaryData(signatureUrl);
                    // column.Item().Height(50).AlignCenter().Image(imgSig).FitArea();
                    column.Item().AlignCenter().Text(examinerName).Bold();
                    column.Item().AlignCenter().Height(100).Element(GetQrCode);
                    column.Spacing(15);
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