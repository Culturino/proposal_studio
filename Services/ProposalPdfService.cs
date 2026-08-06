using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProposalStudio.Services
{
    public record ProposalPdfModel(
        string Reference,
        string ClientName,
        string AdvisorName,
        string BrandName,
        string Model,
        string? Tagline,
        string? Blurb,
        string? Finish,
        string Currency,
        decimal? PriceTotal,
        int ValidityDays,
        string VatMode,
        string[] Features,
        string[] Included,
        string[] Excluded,
        string? AvailabilityNote,
        string ContactLine
    );

    public class ProposalPdfService
    {
        private static readonly string Gold = "#B29064";
        private static readonly string Ink = "#0B0B0C";
        private static readonly string Cream = "#F5F1EA";
        private static readonly string Muted = "#7C766C";

        public ProposalPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] Generate(ProposalPdfModel model)
        {
            var priceLabel = model.PriceTotal.HasValue
                ? $"{model.Currency} {model.PriceTotal.Value:N0}"
                : "On request";

            var vatLine = model.VatMode == "none"
                ? ""
                : "VAT as applicable";

            return Document.Create(container =>
            {
                // 1. Cover
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(40);
                    page.PageColor(Ink);
                    page.DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("HOUSE OF PIANOS")
                            .FontSize(12).FontColor(Gold).LetterSpacing(0.2f);
                        col.Item().PaddingTop(8).Text("A Private Proposal")
                            .FontSize(28).FontColor(Colors.White);
                        col.Item().PaddingTop(24).Text(model.ClientName)
                            .FontSize(20).FontColor(Gold);
                        col.Item().PaddingTop(40).Text($"{model.BrandName} · {model.Model}")
                            .FontSize(16);
                        if (!string.IsNullOrWhiteSpace(model.Tagline))
                        {
                            col.Item().PaddingTop(6).Text(model.Tagline!)
                                .FontSize(12).FontColor(Muted).Italic();
                        }
                        col.Item().PaddingTop(50).Text(model.Reference)
                            .FontSize(10).FontColor(Muted);
                        col.Item().Text(model.ContactLine)
                            .FontSize(9).FontColor(Muted);
                    });
                });

                // 2. The Instrument
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(40);
                    page.PageColor(Cream);
                    page.DefaultTextStyle(x => x.FontColor(Ink).FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("THE INSTRUMENT")
                            .FontSize(10).FontColor(Gold).LetterSpacing(0.15f);
                        col.Item().PaddingTop(8).Text($"{model.BrandName} {model.Model}")
                            .FontSize(22);
                        if (!string.IsNullOrWhiteSpace(model.Tagline))
                        {
                            col.Item().PaddingTop(4).Text(model.Tagline!).Italic().FontColor(Muted);
                        }
                        if (!string.IsNullOrWhiteSpace(model.Blurb))
                        {
                            col.Item().PaddingTop(16).Text(model.Blurb!).FontSize(11).LineHeight(1.4f);
                        }
                        col.Item().PaddingTop(20).Text("Craftsmanship highlights")
                            .FontSize(12).FontColor(Gold);
                        foreach (var f in model.Features.Take(6))
                        {
                            col.Item().PaddingTop(4).Text($"•  {f}");
                        }
                    });
                });

                // 3. Specifications
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(40);
                    page.PageColor(Cream);
                    page.DefaultTextStyle(x => x.FontColor(Ink).FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("SPECIFICATIONS")
                            .FontSize(10).FontColor(Gold).LetterSpacing(0.15f);
                        col.Item().PaddingTop(12).Text(model.Model).FontSize(20);
                        col.Item().PaddingTop(8).Text($"Finish · {model.Finish ?? "—"}");
                        if (!string.IsNullOrWhiteSpace(model.AvailabilityNote))
                        {
                            col.Item().PaddingTop(8).Text(model.AvailabilityNote!).FontColor(Muted);
                        }
                        col.Item().PaddingTop(16).Text("Features").FontColor(Gold);
                        foreach (var f in model.Features)
                        {
                            col.Item().PaddingTop(3).Text($"•  {f}");
                        }
                    });
                });

                // 4. The Investment
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(40);
                    page.PageColor(Cream);
                    page.DefaultTextStyle(x => x.FontColor(Ink).FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("THE INVESTMENT")
                            .FontSize(10).FontColor(Gold).LetterSpacing(0.15f);
                        col.Item().PaddingTop(12).Text(priceLabel).FontSize(28);
                        if (!string.IsNullOrWhiteSpace(vatLine))
                        {
                            col.Item().PaddingTop(4).Text(vatLine).FontColor(Muted).Italic();
                        }
                        col.Item().PaddingTop(8).Text($"Valid for {model.ValidityDays} days from issue");

                        col.Item().PaddingTop(20).Text("Includes").FontColor(Gold);
                        foreach (var i in model.Included)
                        {
                            col.Item().PaddingTop(3).Text($"•  {i}");
                        }

                        col.Item().PaddingTop(16).Text("Not included").FontColor(Gold);
                        foreach (var e in model.Excluded)
                        {
                            col.Item().PaddingTop(3).Text($"•  {e}");
                        }
                    });
                });

                // 5. Closing
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(40);
                    page.PageColor(Ink);
                    page.DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("HOUSE OF PIANOS")
                            .FontSize(12).FontColor(Gold).LetterSpacing(0.2f);
                        col.Item().PaddingTop(24).Text("A pianist grows as good as the piano.")
                            .FontSize(18).Italic();
                        col.Item().PaddingTop(24).Text(model.ContactLine).FontColor(Muted);
                        col.Item().PaddingTop(40).Text($"Prepared by {model.AdvisorName}")
                            .FontSize(12);
                        col.Item().PaddingTop(8).Text(model.Reference).FontColor(Muted).FontSize(10);
                    });
                });
            }).GeneratePdf();
        }

        public static JsonDocument BuildSnapshot(ProposalPdfModel model)
        {
            var json = JsonSerializer.Serialize(model);
            return JsonDocument.Parse(json);
        }
    }
}
