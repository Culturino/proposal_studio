using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Data for the House of Pianos private proposal PDF (matches the 6-page template).
    /// </summary>
    public record ProposalPdfModel(
        string Reference,
        string ClientName,
        string AdvisorName,
        string? AdvisorTitle,
        string BrandName,
        string? BrandBlurb,
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
        string[] Finishes,
        IReadOnlyDictionary<string, string> Dimensions,
        string? AvailabilityNote,
        string BusinessName,
        string? BusinessBlurb,
        string Phone,
        string Website,
        string Instagram,
        string AddressLine,
        string? ProductImagePath,
        DateTimeOffset IssuedAt
    );

    public class ProposalPdfService
    {
        // Aligned with template: dark cover/closing, limestone content, brass accent
        private static readonly string Gold = "#9A8154";
        private static readonly string Ink = "#141312";
        private static readonly string Cream = "#F0EDE8";
        private static readonly string Paper = "#FBFAF7";
        private static readonly string Muted = "#6E6860";
        private static readonly string Line = "#DCD6CB";

        // Same families as the House of Pianos template PDF
        private const string FontDisplay = "HopBookman";      // Bookman Old Style
        private const string FontDisplayBold = "HopBookmanBold";
        private const string FontBody = "HopCambria";         // Cambria
        private const string FontBodyItalic = "HopCambriaItalic";
        private const string FontUi = "HopArial";             // Arial
        private const string FontUiBold = "HopArialBold";

        /// <summary>Template page size: 960×540 pt (~16:9 landscape).</summary>
        private static readonly PageSize Deck = new(960, 540);

        private static bool _fontsReady;

        public ProposalPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
            EnsureTemplateFonts();
        }

        private static void EnsureTemplateFonts()
        {
            if (_fontsReady) return;
            lock (typeof(ProposalPdfService))
            {
                if (_fontsReady) return;

                var dirs = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Fonts"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Fonts"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")
                };

                // Bookman from the template; full Cambria/Arial for complete glyph coverage
                RegisterNamed(FontDisplay, dirs,
                    "BCDHEE_BookmanOldStyle.ttf", "BCDIEE_BookmanOldStyle.ttf", "BOOKOS.TTF");
                RegisterNamed(FontDisplayBold, dirs,
                    "BCDGEE_BookmanOldStyle-Bold.ttf", "BOOKOSB.TTF");
                // Regular Cambria lives in cambria.ttc; use template extract, then bold as last resort
                RegisterNamed(FontBody, dirs,
                    "cambria.ttf", "BCDFEE_Cambria.ttf", "BCDLEE_Cambria.ttf", "cambriab.ttf");
                RegisterNamed(FontBodyItalic, dirs,
                    "cambriai.ttf", "BCDJEE_Cambria-Italic.ttf", "BCDMEE_Cambria-Italic.ttf");
                RegisterNamed(FontUi, dirs, "arial.ttf", "BCDKEE_ArialMT.ttf");
                RegisterNamed(FontUiBold, dirs, "arialbd.ttf", "BCDEEE_Arial-BoldMT.ttf");

                _fontsReady = true;
            }
        }

        private static void RegisterNamed(string family, string[] dirs, params string[] fileNames)
        {
            foreach (var dir in dirs)
            {
                foreach (var file in fileNames)
                {
                    var path = Path.Combine(dir, file);
                    if (!System.IO.File.Exists(path)) continue;
                    try
                    {
                        using var stream = System.IO.File.OpenRead(path);
                        FontManager.RegisterFontWithCustomName(family, stream);
                        return;
                    }
                    catch
                    {
                        // try next candidate
                    }
                }
            }
        }

        public byte[] Generate(ProposalPdfModel model)
        {
            var priceLabel = model.PriceTotal.HasValue
                ? $"{model.Currency} {model.PriceTotal.Value:N0}"
                : "On request";

            var vatHint = model.VatMode == "none"
                ? "Prices as quoted"
                : "VAT as applicable";

            var monthYear = model.IssuedAt.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            var finishLine = string.IsNullOrWhiteSpace(model.Finish) ? "—" : model.Finish!;
            var modelFinishHeader = $"{model.Model.ToUpperInvariant()} · {finishLine.ToUpperInvariant()}";
            var contactCompact = $"{model.Phone}  ·  {model.Website}  ·  {model.Instagram}";
            var coverFoot = $"{contactCompact}    Dubai · {monthYear} · Ref. {model.Reference}";

            var dimRows = model.Dimensions.Count > 0
                ? model.Dimensions
                : new Dictionary<string, string> { ["Length"] = "—", ["Width"] = "—", ["Weight"] = "—" };

            var finishesLine = model.Finishes.Length > 0
                ? string.Join(" · ", model.Finishes)
                : "Quoted on request";

            var features = model.Features.Length > 0
                ? model.Features
                : new[] { "Handcrafted to Steinway standards" };

            var included = model.Included.Length > 0 ? model.Included : Array.Empty<string>();
            var excluded = model.Excluded.Length > 0 ? model.Excluded : Array.Empty<string>();

            return Document.Create(container =>
            {
                // ───────────── 01 Cover ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink);
                    page.Content().Padding(48).Column(col =>
                    {
                        col.Item().Text(Tracked("A PRIVATE PROPOSAL"))
                            .FontFamily(FontUiBold).FontSize(11).FontColor(Gold).LetterSpacing(0.28f);

                        col.Item().PaddingTop(14).Text($"{model.BrandName} · Authorised Representative, United Arab Emirates")
                            .FontFamily(FontBody).FontSize(10).FontColor("#C8C2B6");

                        col.Item().PaddingTop(56).Text(Tracked("PREPARED EXCLUSIVELY FOR"))
                            .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.22f);

                        col.Item().PaddingTop(10).Text(model.ClientName)
                            .FontFamily(FontDisplayBold).FontSize(32).FontColor(Colors.White);

                        col.Item().PaddingTop(48).Text(Tracked("THE INSTRUMENT"))
                            .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.22f);

                        col.Item().PaddingTop(10).Text($"{model.BrandName} — {model.Model}")
                            .FontFamily(FontDisplay).FontSize(22).FontColor(Colors.White);

                        if (!string.IsNullOrWhiteSpace(model.Tagline))
                        {
                            col.Item().PaddingTop(8).Text($"“{TrimQuotes(model.Tagline!)}”")
                                .FontFamily(FontBodyItalic).FontSize(13).FontColor(Gold);
                        }

                        col.Item().ExtendVertical().AlignBottom().Text(coverFoot)
                            .FontFamily(FontUi).FontSize(8).FontColor("#9A948A");
                    });
                });

                // ───────────── 02 The Instrument ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Cream);
                    page.Footer().PaddingHorizontal(48).PaddingBottom(28).Text(Footer("PRIVATE PROPOSAL", 2))
                        .FontFamily(FontUi).FontSize(8).FontColor(Muted);

                    page.Content().Padding(48).Row(row =>
                    {
                        row.RelativeItem(5).Column(col =>
                        {
                            col.Item().Text(Tracked(model.BrandName.ToUpperInvariant()))
                                .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.2f);

                            col.Item().PaddingTop(10).Text(model.Model)
                                .FontFamily(FontDisplayBold).FontSize(30).FontColor(Ink);

                            if (!string.IsNullOrWhiteSpace(model.Tagline))
                            {
                                col.Item().PaddingTop(8).Text(model.Tagline!)
                                    .FontFamily(FontBodyItalic).FontSize(13).FontColor(Muted);
                            }

                            if (!string.IsNullOrWhiteSpace(model.Blurb))
                            {
                                col.Item().PaddingTop(22).Text(model.Blurb!)
                                    .FontFamily(FontBody).FontSize(11).FontColor(Ink).LineHeight(1.45f);
                            }

                            col.Item().PaddingTop(28).Row(feat =>
                            {
                                var half = (int)Math.Ceiling(Math.Min(features.Length, 6) / 2.0);
                                var left = features.Take(half).ToArray();
                                var right = features.Skip(half).Take(6 - left.Length).ToArray();

                                feat.RelativeItem().Column(c =>
                                {
                                    foreach (var f in left)
                                        c.Item().PaddingBottom(6).Text(f).FontFamily(FontBody).FontSize(10).FontColor(Ink);
                                });
                                feat.ConstantItem(24);
                                feat.RelativeItem().Column(c =>
                                {
                                    foreach (var f in right)
                                        c.Item().PaddingBottom(6).Text(f).FontFamily(FontBody).FontSize(10).FontColor(Ink);
                                });
                            });

                            col.Item().PaddingTop(20).Text($"{model.Model} · {finishLine}")
                                .FontFamily(FontUi).FontSize(10).FontColor(Muted);
                        });

                        row.ConstantItem(28);
                        row.RelativeItem(4).AlignMiddle().Element(e => InstrumentHero(e, model));
                    });
                });

                // ───────────── 03 The Brand ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Cream);
                    page.Footer().PaddingHorizontal(48).PaddingBottom(28).Text(Footer("THE BRAND", 3))
                        .FontFamily(FontUi).FontSize(8).FontColor(Muted);

                    page.Content().Padding(48).Row(row =>
                    {
                        row.RelativeItem(5).Column(col =>
                        {
                            col.Item().Text(Tracked("THE BRAND"))
                                .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.22f);

                            col.Item().PaddingTop(12).Text(model.BrandName)
                                .FontFamily(FontDisplayBold).FontSize(30).FontColor(Ink);

                            var brandCopy = !string.IsNullOrWhiteSpace(model.BrandBlurb)
                                ? model.BrandBlurb!
                                : $"{model.BrandName} stands among the world’s most distinguished makers — instruments shaped by tradition, precision, and a voice recognised across concert halls and private residences alike.";

                            col.Item().PaddingTop(22).Text(brandCopy)
                                .FontFamily(FontBody).FontSize(12).FontColor(Ink).LineHeight(1.5f);
                        });

                        row.ConstantItem(28);
                        row.RelativeItem(4).AlignMiddle().Element(e =>
                            SidePlaceholder(e, "BRAND", model.BrandName));
                    });
                });

                // ───────────── 04 The House ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Cream);
                    page.Footer().PaddingHorizontal(48).PaddingBottom(28).Text(Footer("THE HOUSE", 4))
                        .FontFamily(FontUi).FontSize(8).FontColor(Muted);

                    page.Content().Padding(48).Row(row =>
                    {
                        row.RelativeItem(5).Column(col =>
                        {
                            col.Item().Text(Tracked("THE HOUSE"))
                                .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.22f);

                            col.Item().PaddingTop(12).Text(model.BusinessName)
                                .FontFamily(FontDisplayBold).FontSize(30).FontColor(Ink);

                            var businessCopy = !string.IsNullOrWhiteSpace(model.BusinessBlurb)
                                ? model.BusinessBlurb!
                                : $"{model.BusinessName} is a private atelier for the world’s finest instruments — curated for collectors, institutions, and discerning homes across the UAE.";

                            col.Item().PaddingTop(22).Text(businessCopy)
                                .FontFamily(FontBody).FontSize(12).FontColor(Ink).LineHeight(1.5f);
                        });

                        row.ConstantItem(28);
                        row.RelativeItem(4).AlignMiddle().Element(e =>
                            SidePlaceholder(e, "SHOWROOM", model.BusinessName));
                    });
                });

                // ───────────── 05 Gallery ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Cream);
                    page.Footer().PaddingHorizontal(48).PaddingBottom(28).Text(Footer("PRIVATE PROPOSAL", 5))
                        .FontFamily(FontUi).FontSize(8).FontColor(Muted);

                    page.Content().Padding(48).Column(col =>
                    {
                        col.Item().Text(Tracked("GALLERY"))
                            .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.22f);
                        col.Item().PaddingTop(8).Text("Views & Details")
                            .FontFamily(FontDisplayBold).FontSize(26).FontColor(Ink);
                        col.Item().PaddingTop(8).Text("A closer study of the finish, form, and detailing of the instrument reserved for you.")
                            .FontFamily(FontBodyItalic).FontSize(11).FontColor(Muted);

                        col.Item().PaddingTop(28).Row(g =>
                        {
                            GalleryCard(g, model, "01", "Full profile");
                            g.ConstantItem(16);
                            GalleryCard(g, model, "02", "Action & soundboard");
                            g.ConstantItem(16);
                            GalleryCard(g, model, "03", "Keyboard & fallboard");
                        });
                    });
                });

                // ───────────── 06 Specifications ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Cream);
                    page.Footer().PaddingHorizontal(48).PaddingBottom(28).Text(Footer("SPECIFICATIONS", 6))
                        .FontFamily(FontUi).FontSize(8).FontColor(Muted);

                    page.Content().Padding(48).Column(col =>
                    {
                        col.Item().Text(Tracked("SPECIFICATIONS"))
                            .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.22f);
                        col.Item().PaddingTop(10).Text(model.Model)
                            .FontFamily(FontDisplayBold).FontSize(28).FontColor(Ink);

                        var length = dimRows.TryGetValue("length", out var l) ? l
                            : dimRows.TryGetValue("Length", out var l2) ? l2 : null;
                        col.Item().PaddingTop(6).Text(CategoryLine(model, length))
                            .FontFamily(FontBody).FontSize(12).FontColor(Muted);

                        col.Item().PaddingTop(28).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(Tracked("DIMENSIONS & WEIGHT"))
                                    .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.18f);
                                c.Item().PaddingTop(12).Column(dims =>
                                {
                                    foreach (var (label, value) in dimRows)
                                    {
                                        dims.Item().PaddingBottom(8).Row(row =>
                                        {
                                            row.ConstantItem(100).Text(TitleCase(label))
                                                .FontFamily(FontUi).FontSize(10).FontColor(Muted);
                                            row.RelativeItem().Text(value)
                                                .FontFamily(FontBody).FontSize(11).FontColor(Ink);
                                        });
                                    }
                                });
                            });

                            r.ConstantItem(40);

                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(Tracked("CRAFTSMANSHIP"))
                                    .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.18f);
                                c.Item().PaddingTop(12).Column(list =>
                                {
                                    foreach (var f in features)
                                    {
                                        list.Item().PaddingBottom(6).Text(f)
                                            .FontFamily(FontBody).FontSize(11).FontColor(Ink);
                                    }
                                });
                            });
                        });

                        col.Item().PaddingTop(28).LineHorizontal(1).LineColor(Line);
                        col.Item().PaddingTop(16).Text(Tracked("AVAILABLE FINISHES"))
                            .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.18f);
                        col.Item().PaddingTop(8).Text(finishesLine)
                            .FontFamily(FontBody).FontSize(11).FontColor(Ink);
                    });
                });

                // ───────────── 07 The Investment ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Cream);
                    page.Footer().PaddingHorizontal(48).PaddingBottom(28).Text(Footer("THE INVESTMENT", 7))
                        .FontFamily(FontUi).FontSize(8).FontColor(Muted);

                    page.Content().Padding(48).Column(col =>
                    {
                        col.Item().Text(Tracked("THE INVESTMENT"))
                            .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.22f);
                        col.Item().PaddingTop(8).Text("Your Investment")
                            .FontFamily(FontDisplayBold).FontSize(26).FontColor(Ink);

                        col.Item().PaddingTop(22).Text(Tracked(modelFinishHeader))
                            .FontFamily(FontUiBold).FontSize(9).FontColor(Muted).LetterSpacing(0.12f);

                        col.Item().PaddingTop(10).Text(priceLabel)
                            .FontFamily(FontDisplayBold).FontSize(34).FontColor(Ink);
                        col.Item().PaddingTop(4).Text($"Recommended retail · [ {vatHint} ]")
                            .FontFamily(FontBodyItalic).FontSize(10).FontColor(Muted);

                        col.Item().PaddingTop(28).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(Tracked("YOUR INVESTMENT INCLUDES"))
                                    .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.16f);
                                c.Item().PaddingTop(12).Column(list =>
                                {
                                    foreach (var i in included)
                                        list.Item().PaddingBottom(6).Text(i).FontFamily(FontBody).FontSize(11).FontColor(Ink);
                                });
                            });
                            r.ConstantItem(32);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(Tracked("NOT INCLUDED"))
                                    .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.16f);
                                c.Item().PaddingTop(12).Column(list =>
                                {
                                    foreach (var e in excluded)
                                        list.Item().PaddingBottom(6).Text(e).FontFamily(FontBody).FontSize(11).FontColor(Ink);
                                });
                            });
                        });

                        col.Item().PaddingTop(24).Text(
                                $"Prices are quoted in {model.Currency} and remain valid for {model.ValidityDays} days from the date of this proposal. " +
                                "Finishes other than the selected finish, and specialty veneers, are quoted on request.")
                            .FontFamily(FontBody).FontSize(9).FontColor(Muted).LineHeight(1.35f);
                    });
                });

                // ───────────── 08 Closing ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink);
                    page.Content().Padding(48).Column(col =>
                    {
                        col.Item().ExtendVertical().AlignMiddle().Column(mid =>
                        {
                            mid.Item().AlignCenter().Text("“A pianist grows as good as the piano.”")
                                .FontFamily(FontBodyItalic).FontSize(22).FontColor(Colors.White);

                            mid.Item().PaddingTop(28).AlignCenter().Text(
                                    "We would be honoured to welcome you to Steinway Hall Dubai to experience this instrument in person\n— and to accompany you for many years beyond its delivery.")
                                .FontFamily(FontBody).FontSize(11).FontColor("#C8C2B6").AlignCenter().LineHeight(1.45f);
                        });

                        col.Item().AlignBottom().Column(foot =>
                        {
                            foot.Item().AlignCenter().Text(Tracked("STEINWAY HALL DUBAI"))
                                .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.2f);
                            foot.Item().PaddingTop(8).AlignCenter().Text(model.AddressLine)
                                .FontFamily(FontBody).FontSize(9).FontColor("#C8C2B6");
                            foot.Item().PaddingTop(4).AlignCenter().Text(contactCompact)
                                .FontFamily(FontUi).FontSize(9).FontColor("#9A948A");
                            foot.Item().PaddingTop(20).AlignCenter().Text($"Prepared by {model.AdvisorName}")
                                .FontFamily(FontBody).FontSize(11).FontColor(Colors.White);
                            foot.Item().PaddingTop(4).AlignCenter().Text(
                                    $"{model.AdvisorTitle ?? "Sales Advisor"} · {model.BusinessName}")
                                .FontFamily(FontUi).FontSize(9).FontColor(Muted);
                        });
                    });
                });
            }).GeneratePdf();
        }

        private static void ApplyDeck(PageDescriptor page, string background)
        {
            page.Size(Deck);
            page.Margin(0);
            page.PageColor(background);
            page.DefaultTextStyle(x => x.FontColor(Ink).FontSize(11).FontFamily(FontBody));
        }

        private static void InstrumentHero(IContainer container, ProposalPdfModel model)
        {
            container.Background(Paper).Border(1).BorderColor(Line).Padding(12).Column(c =>
            {
                if (!string.IsNullOrWhiteSpace(model.ProductImagePath) && File.Exists(model.ProductImagePath))
                {
                    c.Item().Height(280).Image(model.ProductImagePath!).FitArea();
                }
                else
                {
                    c.Item().Height(280).Background(Ink).AlignCenter().AlignMiddle().Column(inner =>
                    {
                        inner.Item().AlignCenter().Text(model.BrandName)
                            .FontFamily(FontUiBold).FontSize(10).FontColor(Gold).LetterSpacing(0.15f);
                        inner.Item().PaddingTop(8).AlignCenter().Text(model.Model)
                            .FontFamily(FontDisplay).FontSize(18).FontColor(Colors.White);
                    });
                }
            });
        }

        private static void SidePlaceholder(IContainer container, string eyebrow, string title)
        {
            container.Background(Paper).Border(1).BorderColor(Line).Padding(12).Column(c =>
            {
                c.Item().Height(320).Background("#E8E4DC").AlignCenter().AlignMiddle().Column(inner =>
                {
                    inner.Item().AlignCenter().Text(Tracked(eyebrow))
                        .FontFamily(FontUiBold).FontSize(9).FontColor(Gold).LetterSpacing(0.18f);
                    inner.Item().PaddingTop(10).AlignCenter().Text(title)
                        .FontFamily(FontDisplay).FontSize(16).FontColor(Ink);
                    inner.Item().PaddingTop(14).AlignCenter().Text("Image placeholder")
                        .FontFamily(FontUi).FontSize(9).FontColor(Muted);
                });
            });
        }

        private static void GalleryCard(RowDescriptor row, ProposalPdfModel model, string index, string caption)
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Height(220).Background(Paper).Border(1).BorderColor(Line).Element(box =>
                {
                    if (!string.IsNullOrWhiteSpace(model.ProductImagePath) && File.Exists(model.ProductImagePath))
                    {
                        box.Padding(8).Image(model.ProductImagePath!).FitArea();
                    }
                    else
                    {
                        box.Background("#E8E4DC").AlignCenter().AlignMiddle()
                            .Text(index).FontFamily(FontDisplayBold).FontSize(20).FontColor(Gold);
                    }
                });
                c.Item().PaddingTop(10).Text($"{index} · {caption}")
                    .FontFamily(FontUi).FontSize(9).FontColor(Muted).LetterSpacing(0.08f);
            });
        }

        private static string Footer(string section, int page) =>
            $"HOUSE OF PIANOS  {section}  ·  {page:00}";

        private static string Tracked(string value) => value.Trim().ToUpperInvariant();

        private static string TrimQuotes(string value) =>
            value.Trim().Trim('"', '“', '”', '\'');

        private static string TitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').ToLowerInvariant());
        }

        private static string CategoryLine(ProposalPdfModel model, string? length)
        {
            var kind = model.Model.Contains("K-", StringComparison.OrdinalIgnoreCase) ? "Upright Piano"
                : model.Model.Contains("S-", StringComparison.OrdinalIgnoreCase) ? "Baby Grand"
                : model.Model.Contains("D-", StringComparison.OrdinalIgnoreCase) ? "Concert Grand"
                : "Grand Piano";
            return string.IsNullOrWhiteSpace(length) ? kind : $"{kind} · {length}";
        }

        public static JsonDocument BuildSnapshot(ProposalPdfModel model)
        {
            var json = JsonSerializer.Serialize(model);
            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// Ensures a PDF exists on disk for the proposal. Regenerates when missing or forced.
        /// Returns the public relative PdfUrl (e.g. /pdfs/…).
        /// </summary>
        public async Task<string?> EnsureOnDiskAsync(AppDbContext db, Guid proposalId, bool force = false)
        {
            var built = await BuildModelAsync(db, proposalId);
            if (built == null) return null;

            var (proposal, model) = built.Value;
            var absolute = AbsolutePdfPath(proposal.PdfUrl);

            if (!force &&
                !string.IsNullOrWhiteSpace(proposal.PdfUrl) &&
                absolute != null &&
                File.Exists(absolute))
            {
                return proposal.PdfUrl;
            }

            proposal.Snapshot = BuildSnapshot(model);
            var bytes = Generate(model);

            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "pdfs");
            Directory.CreateDirectory(dir);

            var fileName = $"{proposal.Reference}_{SanitizeFilePart(model.ClientName)}_{SanitizeFilePart(model.Model)}.pdf";
            var path = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(path, bytes);

            proposal.PdfUrl = $"/pdfs/{fileName}";
            proposal.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return proposal.PdfUrl;
        }

        public static string? AbsolutePdfPath(string? pdfUrl)
        {
            if (string.IsNullOrWhiteSpace(pdfUrl)) return null;
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                pdfUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }

        public async Task<(Proposal proposal, ProposalPdfModel model)?> BuildModelAsync(AppDbContext db, Guid id)
        {
            var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null) return null;

            var item = await db.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == id);
            if (item == null) return null;

            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
            if (product == null) return null;

            var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == product.BrandId);
            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == proposal.ClientId);
            var advisor = await db.Users.FirstOrDefaultAsync(u => u.Id == proposal.AdvisorId);
            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == proposal.BusinessId);

            var finalPrice = (item.PriceOverride ?? item.UnitPrice ?? 0) * item.Qty;

            var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (product.Dimensions != null)
            {
                foreach (var prop in product.Dimensions.RootElement.EnumerateObject())
                {
                    dimensions[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                }
            }

            var imagePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot", "images", "products",
                $"{product.Id}.png");

            var advisorTitle = advisor?.Role?.ToLowerInvariant() switch
            {
                "admin" => "Director",
                "manager" => "Sales Manager",
                _ => "Sales Advisor"
            };

            var model = new ProposalPdfModel(
                Reference: proposal.Reference,
                ClientName: client?.Name ?? "Client",
                AdvisorName: advisor?.Name ?? "Advisor",
                AdvisorTitle: advisorTitle,
                BrandName: brand?.Name ?? "Steinway & Sons",
                BrandBlurb: brand?.Blurb,
                Model: product.Model,
                Tagline: product.Tagline,
                Blurb: product.Blurb,
                Finish: item.Finish,
                Currency: proposal.Currency,
                PriceTotal: finalPrice > 0 ? finalPrice : proposal.PriceTotal,
                ValidityDays: proposal.ValidityDays,
                VatMode: proposal.VatMode,
                Features: product.Features ?? Array.Empty<string>(),
                Included: item.Included?.Length > 0 ? item.Included : product.DefaultIncludes ?? Array.Empty<string>(),
                Excluded: item.Excluded?.Length > 0 ? item.Excluded : product.DefaultExcludes ?? Array.Empty<string>(),
                Finishes: product.Finishes ?? Array.Empty<string>(),
                Dimensions: dimensions,
                AvailabilityNote: product.AvailabilityNote,
                BusinessName: business?.Name ?? "House of Pianos",
                BusinessBlurb: business?.Blurb,
                Phone: "+971 4 295 2131",
                Website: "houseofpianos-uae.com",
                Instagram: "@houseofpianosuae",
                AddressLine: "Showroom 41, Street A, Al Quoz 1 (Opposite Al Serkal Avenue) · Dubai, United Arab Emirates",
                ProductImagePath: File.Exists(imagePath) ? imagePath : null,
                IssuedAt: proposal.SentAt ?? proposal.UpdatedAt
            );

            return (proposal, model);
        }

        private static string SanitizeFilePart(string value)
        {
            var cleaned = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(cleaned) ? "X" : cleaned.Replace(' ', '-');
        }
    }
}
