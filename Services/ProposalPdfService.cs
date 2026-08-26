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
        DateTimeOffset IssuedAt,
        Guid? ProductId = null
    );

    public class ProposalPdfService
    {
        // Palette sampled directly from the House of Pianos template deck
        private const string Ink = "#0B0B0C";        // dark page background
        private const string Paper = "#FFFFFF";      // light page background
        private const string Cream = "#F6F1E9";      // headings & values on dark
        private const string Gold = "#B29064";       // eyebrows, rules, footer marks
        private const string GoldLight = "#CBA978";  // italic accents & price
        private const string Charcoal = "#1B1B1B";   // headings on light
        private const string BodyInk = "#33312E";    // body copy on light
        private const string Muted = "#726C64";      // captions on light
        private const string MutedDark = "#BDB6A9";  // captions & body on dark
        private const string Faint = "#7C766C";      // cover meta line
        private const string ExcludeInk = "#55514B"; // exclusions list
        private const string CardLine = "#E0D6C5";   // gallery card border
        private const string RowLine = "#23211E";    // specification table rules

        // Same families as the House of Pianos template PDF
        private const string FontDisplay = "HopBookman";      // Bookman Old Style
        private const string FontDisplayBold = "HopBookmanBold";
        private const string FontBody = "HopCambria";         // Cambria / Caladea
        private const string FontBodyItalic = "HopCambriaItalic";
        private const string FontUi = "HopArial";             // Arial
        private const string FontUiBold = "HopArialBold";

        /// <summary>Template page size: 960×540 pt (~16:9 landscape).</summary>
        private static readonly PageSize Deck = new(960, 540);

        // Template geometry (points, measured off the reference deck)
        private const float PageW = 960f;
        private const float PageH = 540f;
        private const float MarginX = 64.8f;
        private const float FooterY = 513.9f;

        /// <summary>Letter spacing as a fraction of font size, matching the template's tracked caps.</summary>
        private const float Track = 0.30f;

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

                // Real Bookman Old Style if licensed and dropped into Fonts/, else the free URW cut.
                // The BCD* files extracted from the template are subsets and would drop glyphs.
                RegisterNamed(FontDisplay, dirs,
                    "BOOKOS.TTF", "BookmanOldStyle.ttf", "Bookman Old Style.ttf",
                    "URWBookman-Light.otf");
                RegisterNamed(FontDisplayBold, dirs,
                    "BOOKOSB.TTF", "BookmanOldStyle-Bold.ttf", "Bookman Old Style Bold.ttf",
                    "URWBookman-Demi.otf");
                // Caladea is metric-compatible with Cambria and free to redistribute.
                RegisterNamed(FontBody, dirs, "Caladea-Regular.ttf", "cambria.ttf");
                RegisterNamed(FontBodyItalic, dirs, "Caladea-Italic.ttf", "cambriai.ttf");
                RegisterNamed(FontUi, dirs, "arial.ttf", "Arimo-Regular.ttf");
                RegisterNamed(FontUiBold, dirs, "arialbd.ttf", "Arimo-Bold.ttf");

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
            var vatHint = model.VatMode == "none" ? "Prices as quoted" : "VAT as applicable";

            var monthYear = model.IssuedAt.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            var finishLine = string.IsNullOrWhiteSpace(model.Finish) ? "—" : model.Finish!;
            var modelFinishHeader = $"{model.Model} · {finishLine}".ToUpperInvariant();
            var contactCompact = $"{model.Phone}    ·    {model.Website}    ·    {model.Instagram}";
            var coverMeta = $"Dubai  ·  {monthYear}  ·  Ref. {model.Reference}";

            var dimRows = model.Dimensions.Count > 0
                ? OrderDimensions(model.Dimensions)
                : new List<KeyValuePair<string, string>>
                {
                    new("Length", "—"), new("Width", "—"), new("Weight", "—")
                };

            var finishesLine = model.Finishes.Length > 0
                ? string.Join("   ·   ", model.Finishes)
                : "Quoted on request";

            var features = model.Features.Length > 0
                ? model.Features
                : new[] { $"Handcrafted by {model.BrandName}" };

            var included = model.Included;
            var excluded = model.Excluded;

            var gallery = GalleryPaths(model);
            var logo = BrandAsset("hop-logo.png");
            var star = BrandAsset("bullet-star.png");
            var check = BrandAsset("bullet-check.png");
            var dash = BrandAsset("bullet-dash.png");

            var disclaimer =
                $"Prices are quoted in {CurrencyName(model.Currency)} and remain valid for {model.ValidityDays} days " +
                $"from the date of this proposal. Finishes other than {finishLine} are quoted on request.";

            return Document.Create(container =>
            {
                // ───────────── 01 Cover ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink, Cream);
                    page.Content().Layers(l =>
                    {
                        Base(l);

                        if (logo != null)
                            At(l, MarginX, 43.2f).Width(58.3f).Image(logo);

                        AtRight(l, 891.4f, 52.1f).Text("A PRIVATE PROPOSAL")
                            .Style(Caps(10.8f, Gold));

                        AtRight(l, 895.8f, 75.0f)
                            .Text($"{model.BrandName}  ·  Authorised Representative, United Arab Emirates")
                            .Style(Serif(10.8f, MutedDark));

                        Hero(At(l, 571.0f, 158.4f).Width(356.4f).Height(351.5f), model, Ink);

                        AtCaps(l, MarginX, 221.8f, 12.3f).Text("PREPARED EXCLUSIVELY FOR")
                            .Style(Caps(12.3f, Gold));

                        At(l, MarginX, 245.7f, 470f).Text(model.ClientName)
                            .Style(Display(38.2f, Cream, bold: true));

                        At(l, 68.8f, 324.4f).Width(151.2f).LineHorizontal(1).LineColor(Gold);

                        AtCaps(l, MarginX, 346.4f, 10.8f).Text("THE INSTRUMENT").Style(Caps(10.8f, Gold));

                        At(l, MarginX, 360.4f, 470f).Text($"{model.BrandName} — {model.Model}")
                            .Style(Display(20.9f, Cream));

                        if (!string.IsNullOrWhiteSpace(model.Tagline))
                            At(l, MarginX, 406.2f, 470f).Text($"“{TrimQuotes(model.Tagline!)}”")
                                .Style(Italic(13.7f, GoldLight));

                        At(l, MarginX, 502.9f).Text(contactCompact).Style(Contact(9.4f, MutedDark));
                        AtRight(l, 894.5f, 503.2f).Text(coverMeta).Style(Contact(9.4f, Faint));
                    });
                });

                // ───────────── 02 The Instrument ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Paper, BodyInk);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, model.BrandName.ToUpperInvariant(), model.Model, 40.4f, Charcoal, titleY: 75.0f);

                        if (!string.IsNullOrWhiteSpace(model.Tagline))
                            At(l, MarginX, 148.2f, 445f).Text(model.Tagline!).Style(Italic(15.1f, Gold));

                        At(l, MarginX, 180.0f, 442f).Column(c =>
                        {
                            foreach (var para in Paragraphs(model.Blurb))
                                c.Item().PaddingBottom(34).Text(para)
                                    .Style(Serif(13.7f, BodyInk)).LineHeight(1.42f);
                        });

                        At(l, MarginX, 360.1f, 440f).Row(r =>
                        {
                            var shown = features.Take(4).ToArray();
                            var half = (int)Math.Ceiling(shown.Length / 2.0);
                            r.ConstantItem(219.6f).Column(c => Bullets(
                                c, shown.Take(half), star, 11.5f, 19.5f, 43.2f, 12.2f, Charcoal, 1.1f));
                            r.RelativeItem().Column(c => Bullets(
                                c, shown.Skip(half), star, 11.5f, 19.5f, 43.2f, 12.2f, Charcoal, 1.1f));
                        });

                        Hero(At(l, 596.2f, 111.6f).Width(313.2f).Height(309.0f), model, Paper);

                        At(l, 596.2f, 428.8f, 313.2f).AlignCenter()
                            .Text($"{model.Model}  ·  {finishLine}").Style(Italic(10.8f, Muted));

                        Footer(l, "PRIVATE PROPOSAL", 2, Muted);
                    });
                });

                // ───────────── 03 Gallery ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Paper, BodyInk);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, "GALLERY", "Views & Details", 33.9f, Charcoal, titleY: 75.9f);

                        At(l, MarginX, 143.2f, 830f)
                            .Text("A closer study of the finish, form, and detailing of the instrument reserved for you.")
                            .Style(Italic(13.0f, Muted));

                        GalleryCard(At(l, 65.2f, 184.0f).Width(399.6f).Height(284.5f), gallery[0], model);
                        GalleryCard(At(l, 486.4f, 184.0f).Width(403.2f).Height(133.2f), gallery[1], model);
                        GalleryCard(At(l, 486.4f, 333.1f).Width(403.2f).Height(133.2f), gallery[2], model);

                        At(l, 65.2f, 474.2f, 399.6f).AlignCenter()
                            .Text("01  ·  Full profile").Style(Caption(9.4f, Muted));

                        At(l, 486.4f, 474.2f, 403.2f).AlignCenter().Row(r =>
                        {
                            r.AutoItem().Text("02  ·  Action & soundboard").Style(Caption(9.4f, Muted));
                            r.ConstantItem(40);
                            r.AutoItem().Text("03  ·  Keyboard & fallboard").Style(Caption(9.4f, Muted));
                        });

                        Footer(l, "PRIVATE PROPOSAL", 3, Muted);
                    });
                });

                // ───────────── 04 Specifications ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink, Cream);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, "SPECIFICATIONS", model.Model, 31.7f, Cream, titleY: 73.1f);

                        At(l, MarginX, 138.9f, 445f).Text(CategoryLine(model, Dimension(dimRows, "length")))
                            .Style(Italic(13.0f, GoldLight));

                        AtCaps(l, MarginX, 188.4f, 10.8f).Text("DIMENSIONS & WEIGHT").Style(Caps(10.8f, Gold));

                        At(l, MarginX, 227.9f, 396.4f).Column(c =>
                        {
                            foreach (var (label, value) in dimRows.Take(4))
                            {
                                c.Item().PaddingBottom(9.4f).BorderBottom(1).BorderColor(RowLine).Row(r =>
                                {
                                    r.ConstantItem(151.3f).Text(TitleCase(label)).Style(Sans(11.5f, MutedDark));
                                    r.RelativeItem().Text(value).Style(Serif(13.7f, Cream));
                                });
                                c.Item().Height(15.2f);
                            }
                        });

                        AtCaps(l, 515.1f, 188.4f, 10.8f).Text("CRAFTSMANSHIP").Style(Caps(10.8f, Gold));

                        At(l, 514.8f, 224.7f, 380f).Column(c => Bullets(
                            c, features.Take(7), star, 10.1f, 19.7f, 33.8f, 13.0f, Cream, -0.4f));

                        AtCaps(l, MarginX, 451.6f, 10.8f).Text("AVAILABLE FINISHES").Style(Caps(10.8f, Gold));
                        At(l, MarginX, 474.1f, 830f).Text(finishesLine).Style(Serif(12.2f, MutedDark));

                        Footer(l, "SPECIFICATIONS", 4, MutedDark);
                    });
                });

                // ───────────── 05 The Investment ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Paper, BodyInk);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, "THE INVESTMENT", "Your Investment", 33.9f, Charcoal, titleY: 76.1f);

                        // Drawn as SVG because QuestPDF 2024.12 has no corner radius on Background().
                        At(l, MarginX, 151.2f).Width(385.2f).Height(126.1f).Svg(
                            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 385 126'>" +
                            $"<rect width='385' height='126' rx='5.4' fill='{Ink}'/></svg>");

                        At(l, MarginX, 151.2f).Width(385.2f).Height(126.1f)
                            .PaddingLeft(20.2f).PaddingTop(21.4f).Column(c =>
                            {
                                c.Item().Text(modelFinishHeader).Style(Caps(9.4f, MutedDark));
                                c.Item().PaddingTop(2).Text(t =>
                                {
                                    if (model.PriceTotal.HasValue)
                                    {
                                        t.Span($"{model.Currency} ").Style(Display(20.2f, GoldLight, bold: true));
                                        t.Span(model.PriceTotal.Value.ToString("N0", CultureInfo.InvariantCulture))
                                            .Style(Display(33.9f, GoldLight, bold: true));
                                    }
                                    else
                                    {
                                        t.Span("On request").Style(Display(26f, GoldLight, bold: true));
                                    }
                                });
                                c.Item().PaddingTop(12).Text($"Recommended retail  ·  [ {vatHint} ]")
                                    .Style(Italic(10.8f, MutedDark));
                            });

                        AtCaps(l, MarginX, 305.9f, 10.8f).Text("NOT INCLUDED").Style(Caps(10.8f, Gold));
                        At(l, 66.2f, 337.0f, 385f).Column(c => Bullets(
                            c, excluded.Take(4), dash, 11.6f, 20.2f, 33.2f, 12.3f, ExcludeInk, 1.1f));

                        AtCaps(l, 486.2f, 156.0f, 10.8f).Text("YOUR INVESTMENT INCLUDES").Style(Caps(10.8f, Gold));
                        At(l, 487.4f, 187.2f, 405f).Column(c => Bullets(
                            c, included.Take(6), check, 14.4f, 25.5f, 36.0f, 13.0f, Charcoal, 2.5f));

                        At(l, MarginX, 472.2f, 830f).Text(disclaimer).Style(Italic(10.8f, Muted));

                        Footer(l, "THE INVESTMENT", 5, Muted);
                    });
                });

                // ───────────── 06 Closing ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink, Cream);
                    page.Content().Layers(l =>
                    {
                        Base(l);

                        if (logo != null)
                            AtCenter(l, 57.6f, 87.1f).Image(logo);

                        AtCenter(l, 216.0f, 700f).AlignCenter()
                            .Text("“A pianist grows as good as the piano.”")
                            .Style(Italic(18.7f, GoldLight));

                        AtCenter(l, 255.7f, 585f).Text(
                                "We would be honoured to welcome you to Steinway Hall Dubai to experience this " +
                                "instrument in person — and to accompany you for many years beyond its delivery.")
                            .Style(Serif(13.7f, MutedDark)).LineHeight(1.42f).AlignCenter();

                        AtCenter(l, 338.8f, 128.9f).LineHorizontal(1).LineColor(Gold);

                        AtCenter(l, 361.0f, 700f).AlignCenter()
                            .Text("STEINWAY HALL DUBAI").Style(Caps(12.2f, Gold));

                        AtCenter(l, 384.2f, 700f).AlignCenter()
                            .Text(model.AddressLine).Style(Serif(12.3f, Cream));

                        AtCenter(l, 408.4f, 700f).AlignCenter()
                            .Text(contactCompact).Style(Contact(10.8f, MutedDark));

                        AtCenter(l, 460.9f, 700f).AlignCenter().Text(t =>
                        {
                            t.Span("Prepared by  ").Style(Serif(13.0f, MutedDark));
                            t.Span(model.AdvisorName).Style(Serif(13.0f, Cream));
                        });

                        AtCenter(l, 487.0f, 700f).AlignCenter()
                            .Text($"{model.AdvisorTitle ?? "Sales Advisor"}  ·  {model.BusinessName}")
                            .Style(Sans(9.4f, "#8A8479").LetterSpacing(0.12f));
                    });
                });
            }).GeneratePdf();
        }

        // ───────────────────────── layout primitives ─────────────────────────

        /// <summary>Full-bleed sizing layer; every other layer is absolutely positioned on top.</summary>
        private static void Base(LayersDescriptor layers) =>
            layers.PrimaryLayer().Width(PageW).Height(PageH).Background(Colors.Transparent);

        private static IContainer At(LayersDescriptor layers, float x, float y) =>
            layers.Layer().PaddingLeft(x).PaddingTop(y).AlignTop().AlignLeft();

        private static IContainer At(LayersDescriptor layers, float x, float y, float width) =>
            layers.Layer().PaddingLeft(x).PaddingTop(y).AlignTop().Width(width);

        private static IContainer AtRight(LayersDescriptor layers, float rightEdge, float y) =>
            layers.Layer().PaddingRight(PageW - rightEdge).PaddingTop(y).AlignTop().AlignRight();

        private static IContainer AtCenter(LayersDescriptor layers, float y, float width) =>
            layers.Layer().PaddingTop(y).AlignTop().AlignCenter().Width(width);

        /// <summary>
        /// Tracked caps start half a letter-space in from the block edge, so pull the block
        /// back by that much to land the first glyph on the template's left margin.
        /// </summary>
        private static IContainer AtCaps(LayersDescriptor layers, float x, float y, float size) =>
            At(layers, x - size * Track / 2f, y);

        private static TextStyle Caps(float size, string color) => TextStyle.Default
            .FontFamily(FontUiBold).FontSize(size).FontColor(color).LetterSpacing(Track);

        private static TextStyle Sans(float size, string color) => TextStyle.Default
            .FontFamily(FontUi).FontSize(size).FontColor(color);

        /// <summary>Phone / web / handle lines, lightly tracked as in the template.</summary>
        private static TextStyle Contact(float size, string color) => Sans(size, color).LetterSpacing(0.105f);

        /// <summary>Gallery plate captions, tracked a little wider than body sans.</summary>
        private static TextStyle Caption(float size, string color) => Sans(size, color).LetterSpacing(0.2f);

        private static TextStyle Serif(float size, string color) => TextStyle.Default
            .FontFamily(FontBody).FontSize(size).FontColor(color);

        private static TextStyle Italic(float size, string color) => TextStyle.Default
            .FontFamily(FontBodyItalic).FontSize(size).FontColor(color);

        private static TextStyle Display(float size, string color, bool bold = false) => TextStyle.Default
            .FontFamily(bold ? FontDisplayBold : FontDisplay).FontSize(size).FontColor(color);

        /// <summary>
        /// Gold eyebrow plus Bookman page title. <paramref name="titleY"/> is the block offset that
        /// lands the cap height on the template's baseline; it sits above the template's own y
        /// because the display font's line box is taller than its glyphs.
        /// </summary>
        private static void SectionHead(LayersDescriptor layers, string eyebrow, string title,
            float titleSize, string titleColor, float titleY)
        {
            AtCaps(layers, MarginX, 56.0f, 12.3f).Text(eyebrow).Style(Caps(12.3f, Gold));
            At(layers, MarginX, titleY, 620f).Text(title).Style(Display(titleSize, titleColor, bold: true));
        }

        private static void Footer(LayersDescriptor layers, string section, int page, string sectionColor)
        {
            AtCaps(layers, MarginX, FooterY, 7.9f).Text("HOUSE OF PIANOS").Style(Caps(7.9f, Gold));
            AtRight(layers, PageW - MarginX, FooterY)
                .Text($"{section}  ·  {page:00}")
                .Style(Sans(7.9f, sectionColor).LetterSpacing(Track));
        }

        private static void Bullets(ColumnDescriptor col, IEnumerable<string> items, string? icon,
            float iconSize, float textOffset, float pitch, float fontSize, string color, float textNudge)
        {
            foreach (var item in items)
            {
                col.Item().Height(pitch).Row(r =>
                {
                    r.ConstantItem(textOffset).Element(e =>
                    {
                        if (icon != null) e.Width(iconSize).Image(icon);
                        else e.PaddingTop(fontSize * 0.4f).Width(4).Height(4).Background(Gold);
                    });
                    r.RelativeItem().PaddingTop(textNudge).Text(item).Style(Serif(fontSize, color));
                });
            }
        }

        /// <summary>Product cut-out, unframed, exactly as the template presents it.</summary>
        private static void Hero(IContainer container, ProposalPdfModel model, string background)
        {
            if (!string.IsNullOrWhiteSpace(model.ProductImagePath) && File.Exists(model.ProductImagePath))
            {
                container.Image(model.ProductImagePath!).FitArea();
                return;
            }

            var onDark = background == Ink;
            container.AlignCenter().AlignMiddle().Column(inner =>
            {
                inner.Item().AlignCenter().Text(model.BrandName).Style(Caps(10f, Gold));
                inner.Item().PaddingTop(8).AlignCenter().Text(model.Model)
                    .Style(Display(18f, onDark ? Cream : Charcoal));
            });
        }

        private static void GalleryCard(IContainer container, string? imagePath, ProposalPdfModel model)
        {
            container.Background(Paper).Border(1).BorderColor(CardLine).Padding(5.4f)
                .AlignCenter().AlignMiddle().Element(inner =>
                {
                    if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                        inner.Image(imagePath!).FitArea();
                    else
                        inner.Text(model.Model).Style(Display(14f, CardLine));
                });
        }

        /// <summary>
        /// Three gallery slots: the primary render plus optional "-2"/"-3" siblings on disk,
        /// falling back to the primary so the page never shows empty frames.
        /// </summary>
        private static string?[] GalleryPaths(ProposalPdfModel model)
        {
            var primary = model.ProductImagePath;
            if (string.IsNullOrWhiteSpace(primary) || !File.Exists(primary))
                return new string?[] { null, null, null };

            string Variant(string suffix)
            {
                var dir = Path.GetDirectoryName(primary)!;
                var candidate = Path.Combine(dir,
                    $"{Path.GetFileNameWithoutExtension(primary)}{suffix}{Path.GetExtension(primary)}");
                return File.Exists(candidate) ? candidate : primary;
            }

            return new string?[] { primary, Variant("-2"), Variant("-3") };
        }

        private static string? BrandAsset(string fileName)
        {
            foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var path = Path.Combine(root, "wwwroot", "brand", fileName);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static IEnumerable<string> Paragraphs(string? blurb)
        {
            if (string.IsNullOrWhiteSpace(blurb)) yield break;
            foreach (var part in blurb.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static string? Dimension(IEnumerable<KeyValuePair<string, string>> dimensions, string key) =>
            dimensions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        private static string CurrencyName(string code) => code.ToUpperInvariant() switch
        {
            "AED" => "UAE Dirham",
            "USD" => "US Dollars",
            "EUR" => "Euro",
            "GBP" => "Pounds Sterling",
            _ => code
        };

        private static void ApplyDeck(PageDescriptor page, string background, string defaultInk)
        {
            page.Size(Deck);
            page.Margin(0);
            page.PageColor(background);
            page.DefaultTextStyle(x => x.FontColor(defaultInk).FontSize(11).FontFamily(FontBody));
        }

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
            if (string.IsNullOrWhiteSpace(length)) return kind;

            // The header carries the metric figure only; imperial stays in the table.
            var metric = length!.Split('(')[0].Trim();
            return $"{kind}  ·  {metric}";
        }

        private static readonly JsonSerializerOptions SnapshotJson = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        /// <summary>Persisted shape of a proposal PDF (all content frozen at save time).</summary>
        private sealed class SnapshotPayload
        {
            public string Reference { get; set; } = "";
            public string ClientName { get; set; } = "Client";
            public string AdvisorName { get; set; } = "Advisor";
            public string? AdvisorTitle { get; set; }
            public string BrandName { get; set; } = "Steinway & Sons";
            public string? BrandBlurb { get; set; }
            public string Model { get; set; } = "";
            public string? Tagline { get; set; }
            public string? Blurb { get; set; }
            public string? Finish { get; set; }
            public string Currency { get; set; } = "AED";
            public decimal? PriceTotal { get; set; }
            public int ValidityDays { get; set; } = 14;
            public string VatMode { get; set; } = "line";
            public string[]? Features { get; set; }
            public string[]? Included { get; set; }
            public string[]? Excluded { get; set; }
            public string[]? Finishes { get; set; }
            public Dictionary<string, string>? Dimensions { get; set; }
            public string? AvailabilityNote { get; set; }
            public string BusinessName { get; set; } = "House of Pianos";
            public string? BusinessBlurb { get; set; }
            public string Phone { get; set; } = "";
            public string Website { get; set; } = "";
            public string Instagram { get; set; } = "";
            public string AddressLine { get; set; } = "";
            public string? ProductImagePath { get; set; }
            public DateTimeOffset IssuedAt { get; set; }
            public Guid? ProductId { get; set; }
        }

        public static JsonDocument BuildSnapshot(ProposalPdfModel model)
        {
            var json = JsonSerializer.Serialize(model, SnapshotJson);
            return JsonDocument.Parse(json);
        }

        public static ProposalPdfModel? TryLoadModelFromSnapshot(JsonDocument? snapshot)
        {
            if (snapshot == null) return null;
            try
            {
                var p = JsonSerializer.Deserialize<SnapshotPayload>(
                    snapshot.RootElement.GetRawText(),
                    SnapshotJson);
                if (p == null || string.IsNullOrWhiteSpace(p.Reference) || string.IsNullOrWhiteSpace(p.Model))
                    return null;

                return new ProposalPdfModel(
                    Reference: p.Reference,
                    ClientName: p.ClientName,
                    AdvisorName: p.AdvisorName,
                    AdvisorTitle: p.AdvisorTitle,
                    BrandName: p.BrandName,
                    BrandBlurb: p.BrandBlurb,
                    Model: p.Model,
                    Tagline: p.Tagline,
                    Blurb: p.Blurb,
                    Finish: p.Finish,
                    Currency: string.IsNullOrWhiteSpace(p.Currency) ? "AED" : p.Currency,
                    PriceTotal: p.PriceTotal,
                    ValidityDays: p.ValidityDays <= 0 ? 14 : p.ValidityDays,
                    VatMode: string.IsNullOrWhiteSpace(p.VatMode) ? "line" : p.VatMode,
                    Features: p.Features ?? Array.Empty<string>(),
                    Included: p.Included ?? Array.Empty<string>(),
                    Excluded: p.Excluded ?? Array.Empty<string>(),
                    Finishes: p.Finishes ?? Array.Empty<string>(),
                    Dimensions: CleanDimensions(p.Dimensions),
                    AvailabilityNote: p.AvailabilityNote,
                    BusinessName: p.BusinessName,
                    BusinessBlurb: p.BusinessBlurb,
                    Phone: p.Phone,
                    Website: p.Website,
                    Instagram: p.Instagram,
                    AddressLine: p.AddressLine,
                    ProductImagePath: p.ProductImagePath,
                    IssuedAt: p.IssuedAt == default ? DateTimeOffset.UtcNow : p.IssuedAt,
                    ProductId: p.ProductId
                );
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Re-resolve product image from disk using ProductId when the stored path is stale.
        /// Text/pricing content always comes from the snapshot.
        /// </summary>
        private static ProposalPdfModel WithResolvedImage(ProposalPdfModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.ProductImagePath) && File.Exists(model.ProductImagePath))
                return model;

            if (model.ProductId is Guid productId)
            {
                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot", "images", "products",
                    $"{productId}.png");
                if (File.Exists(path))
                    return model with { ProductImagePath = path };
            }

            return model with { ProductImagePath = null };
        }

        /// <summary>
        /// Rebuilds the PDF content model from the latest proposal + catalog data and
        /// persists it on the proposal.snapshot. Call on save / update / send.
        /// </summary>
        public async Task<bool> RefreshContentAsync(AppDbContext db, Guid proposalId)
        {
            var built = await BuildModelAsync(db, proposalId);
            if (built == null) return false;

            var (proposal, model) = built.Value;
            proposal.Snapshot = BuildSnapshot(model);
            proposal.PdfUrl = null; // content snapshot is the source of truth
            proposal.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Renders PDF from the stored snapshot when present (immutable view).
        /// Falls back to building + saving a snapshot only if none exists yet.
        /// </summary>
        public async Task<(byte[] Bytes, string FileName)?> RenderAsync(AppDbContext db, Guid proposalId)
        {
            var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId);
            if (proposal == null) return null;

            var model = TryLoadModelFromSnapshot(proposal.Snapshot);

            if (model == null)
            {
                // Legacy / never saved with content — build once and freeze
                var built = await BuildModelAsync(db, proposalId);
                if (built == null) return null;
                model = built.Value.model;
                proposal.Snapshot = BuildSnapshot(model);
                proposal.PdfUrl = null;
                proposal.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            model = WithResolvedImage(model);
            var bytes = Generate(model);
            var fileName =
                $"{proposal.Reference}_{SanitizeFilePart(model.ClientName)}_{SanitizeFilePart(model.Model)}.pdf";
            return (bytes, fileName);
        }

        [Obsolete("PDFs are generated on demand; use RenderAsync / RefreshContentAsync.")]
        public async Task<string?> EnsureOnDiskAsync(AppDbContext db, Guid proposalId, bool force = false)
        {
            var rendered = await RenderAsync(db, proposalId);
            return rendered == null ? null : "on-demand";
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

            var dimensions = ReadDimensions(product.Dimensions);

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
                IssuedAt: proposal.SentAt ?? proposal.UpdatedAt,
                ProductId: product.Id
            );

            return (proposal, model);
        }

        /// <summary>
        /// Postgres jsonb sorts object keys by length, so the stored order is not the reading
        /// order. Impose the sequence the specifications table expects and let anything
        /// unrecognised follow in its original position.
        /// </summary>
        private static List<KeyValuePair<string, string>> OrderDimensions(
            IReadOnlyDictionary<string, string> dimensions)
        {
            var order = new[] { "length", "height", "width", "depth", "weight", "setting" };

            return dimensions
                .OrderBy(entry =>
                {
                    var index = Array.IndexOf(order, entry.Key.ToLowerInvariant());
                    return index < 0 ? order.Length : index;
                })
                .ToList();
        }

        /// <summary>
        /// Snapshots frozen before dimensions were sanitised can still carry the "RootElement"
        /// wrapper, so drop it here too and restore the case-insensitive lookup.
        /// </summary>
        private static Dictionary<string, string> CleanDimensions(Dictionary<string, string>? stored)
        {
            var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (stored == null) return dimensions;

            foreach (var (label, value) in stored)
            {
                if (string.Equals(label, "RootElement", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(value)) continue;
                dimensions[label] = value.Trim();
            }

            return dimensions;
        }

        /// <summary>
        /// Reads the product's jsonb dimensions into label/value pairs, skipping anything the
        /// specifications table cannot render: non-object roots, nested values, and the
        /// "RootElement" wrapper left behind by rows saved through a mis-serialised client.
        /// </summary>
        private static Dictionary<string, string> ReadDimensions(JsonDocument? source)
        {
            var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source == null || source.RootElement.ValueKind != JsonValueKind.Object)
                return dimensions;

            foreach (var prop in source.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "RootElement", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.ToString(),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(value))
                    dimensions[prop.Name] = value!.Trim();
            }

            return dimensions;
        }

        private static string SanitizeFilePart(string value)
        {
            var cleaned = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(cleaned) ? "X" : cleaned.Replace(' ', '-');
        }
    }
}
