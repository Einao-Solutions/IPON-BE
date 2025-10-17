using patentdesign.Models;using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

public class OtherAck(OtherPaymentModel other):IDocument
{
    private OtherPaymentModel otherPaymentInfo { get; set; } = other;
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(50);
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

    void ComposeContent(IContainer container)
    {
        container
            .PaddingVertical(10)
            .Column(column =>
            {
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA");
                column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                column.Item().AlignCenter().Text("PATENTS AND DESIGNS ACT CAP 344, LFN 1990");
                column.Item().Height(10);
                column.Item().AlignCenter().Text("ACKNOWLEDGEMENT LETTER");
                column.Item().Height(20);
                column.Item().Text(
                    $"This letter serves as official acknowledgment that payment"
                    + $"from {otherPaymentInfo.name},  in the amount of NGN {otherPaymentInfo.amount} for {otherPaymentInfo.ServiceName} has been received.");
                column.Item().Text("Please retain this acknowledgment for your records.");
                column.Item().Height(30);
                column.Item().Element(Block).Text("Payment Date").Style(TextStyle.Default.SemiBold());
                column.Item().Element(Block).Text(otherPaymentInfo.date.ToString());
            });
    }
}