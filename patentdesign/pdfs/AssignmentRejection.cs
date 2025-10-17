using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class AssignmentRejection(AssignmentCertificateType assDets, string reason) : IDocument
    {
        private AssignmentCertificateType assDets { get; set; } = assDets;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Height(30).AlignRight().Image("assets/ministry.png").FitArea();
                });
                page.Header().Text(assDets.fileNumber);
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
                    column.Item().Height(40).AlignCenter(). Image("assets/ministry.png").FitArea();
                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("CERTIFICATE OF ASSIGNMENT REJECTION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(5);
                    column.Item().Text($"To: {assDets.applicantName} ")
                        .Style(TextStyle.Default.Bold());
                    column.Item().Text($"C/O {assDets.CorrespondenceType.name}").Style(TextStyle.Default.Bold());
                    column.Item().Text($"{assDets.CorrespondenceType.address}").Style(TextStyle.Default.Bold());
                    column.Item().Height(5);
                    column.Item().Text($"I hereby notify you that the assignment application has been REJECTED: {assDets.fileNumber+ConstantValues.AssPassage2}");
                    column.Item().Height(5);
                    column.Item().Text($"Assignor name: {assDets.assignmentType.assignorName}").Style(TextStyle.Default.Bold());
                    column.Item().Text($"Assignor Address: {assDets.assignmentType.assignorAddress}").Style(TextStyle.Default.Bold());
                    column.Item().Height(5);
                    column.Item().Text($"Assignee name: {assDets.assignmentType.assigneeName}").Style(TextStyle.Default.Bold());
                    column.Item().Text($"Assignee Address: {assDets.assignmentType.assigneeAddress}").Style(TextStyle.Default.Bold());
                    column.Item().Height(5);
                    column.Item().Text($"This application has hereby been REJECTED, with reason: {reason}. Received on: {assDets.paymentDate.ToString("D")} for application dated: {assDets.paymentDate.ToString("D")}").Style(TextStyle.Default.Bold());
                    column.Item().Height(10);
                    column.Item().Text($"Witness my hand this: {DateTime.Now.ToString("D")}").Style(TextStyle.Default.Bold());
                    column.Item().Height(5);
                    var imgSig = Image.FromBinaryData(assDets.examinerSignature);
                    column.Item().Height(40).AlignCenter().Image(imgSig).FitArea();
                    column.Item().AlignCenter().Text("Patent Officer, Abuja, Nigeria.");
                    column.Item().AlignCenter().Text($"{assDets.examinerName}").Bold();
                    column.Item().AlignCenter().Text($"For Registrar Patents and Designs");
                    column.Spacing(15);
                });
        }
       
    }
}