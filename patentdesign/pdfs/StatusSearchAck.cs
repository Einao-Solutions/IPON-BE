using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign
{
    public class StatusSearchAck(StatusRequests data) : IDocument
    {
                
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
                    column.Item().AlignCenter().Text("STATUS SEARCH ACKNOWLEDGEMENT LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                    column.Item().Height(10);
                    column.Item()
                        .Text(
                            $"Your application to view the status for the file with file number {data.fileId} has been received.");
                    column.Item().AlignCenter().PaddingTop(50).Text("YOUR APPLICATION HAS BEEN RECEIVED AND THE RESULTS ARE READY").ExtraBold().FontColor(Colors.Red.Darken2);
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                    column.Spacing(15);
                });
        }
        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode("url", QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                container.Image(qrCodeImage).FitArea();
            }
        }
    }
}