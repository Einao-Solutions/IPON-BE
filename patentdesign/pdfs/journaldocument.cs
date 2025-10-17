using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class JournalDocument(List<PublicationType> models, FileTypes type,DateTime start, DateTime end) : IDocument
    {
        private List<PublicationType> models { get; set; } = models;
         public void Compose(IDocumentContainer container)
         {
             var title = $"{type.ToString()} publications between {start.ToString("D")} and {end.ToString("D")}";
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Header().Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text(title);
                });
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Height(30).AlignRight().Image("assets/ministry.png").FitArea();
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
            var title =
                type == FileTypes.Design ? "Title of Design" : type == FileTypes.Patent ? "Title of Invention" : "";
            var creatorInventorType = type switch
            {
                FileTypes.Design => "Design Creators",
                FileTypes.Patent => "Patent Inventors",
                _ => ""
            };


            container
                .PaddingVertical(10)
                .Column(column =>
                {
                    foreach (var model in models)
                    {
                        column.Item().Text(models.IndexOf(model) + 1);
                        column.Item().Text(text =>
                        {
                            text.Span("Publication Date").Bold();
                            text.Span(model.Date.ToString("D"));
                            text.EmptyLine();
                            text.Span("File Number").Bold();
                            text.Span(model.FileId);
                            text.EmptyLine();
                            text.Span("System ID").Bold();
                            text.Span(model.Id);
                            text.EmptyLine();
                        });
                        column.Item().Text(title)
                            .Style(TextStyle.Default.Bold());
                        column.Item().Text(model.Title).Justify();
                        column.Item().Text("Applicants").Bold();
                        column.Item().Text(text =>
                        {
                            foreach (var applicant in model.Applicants)
                            {
                                text.Span($"{applicant.Name}, {applicant.Phone}, {applicant.Email}, {applicant.Address}, {applicant.country}");
                                text.EmptyLine();
                            }
                        });
                        column.Item().Text(creatorInventorType).Bold();
                        column.Item().Text(text =>
                        {
                            foreach (var applicant in model.inventorsCreators)
                            {
                                text.Span($"{applicant.Name}, {applicant.Phone}, {applicant.Email}, {applicant.Address}, {applicant.country}");
                                text.EmptyLine();
                            }
                        });
                        column.Item().Text("Correspondence").Bold();
                        column.Item().Text(text =>
                        {
                            text.Span($"{model.Correspondence.name}, {model.Correspondence.state}, {model.Correspondence.phone}, {model.Correspondence.email}, {model.Correspondence.address}");
                            text.EmptyLine();
                        });
                        
                        if (type == FileTypes.Design)
                        {
                            column.Item().Text("Design Representations");

                            foreach (var image in model.ImagesUrl)
                            {
                                var img = Image.FromBinaryData(image);
                                column.Item().Height(100).AlignCenter().Image(img).FitArea();
                            }
                        }

                        column.Spacing(15);
                    }
                });
        }
    }
}