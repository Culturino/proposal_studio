"""Add business/brand blurb columns and fill narrative copy + product blurbs."""
import psycopg2

CONN = "host=localhost port=5432 dbname=proposal_studio user=postgres password=ilovesatan"

BUSINESS_BLURB = (
    "House of Pianos is a private atelier for the world’s finest instruments — from concert "
    "grands to intimate uprights — curated for collectors, institutions, and discerning homes "
    "across the UAE. Our advisors pair each client with the right voice, finish, and presentation, "
    "then accompany every proposal from first sitting through white-glove delivery and lifelong care."
)

BRAND_BLURBS = {
    "Steinway & Sons": (
        "Founded in 1853, Steinway & Sons remains the benchmark of concert and home pianos — "
        "hand-built instruments chosen by the majority of performing artists worldwide. Each grand "
        "and upright carries the company’s continuous bent rim, diaphragmatic soundboard, and "
        "uncompromising action, delivering the unmistakable Steinway voice across generations."
    ),
    "Blüthner": (
        "Crafted in Leipzig since 1853, Blüthner pianos are prized for their golden, singing tone "
        "and the distinctive Aliquot stringing that lends an ethereal shimmer to the treble. "
        "Each instrument is built by hand for players who seek warmth, colour, and a romantic "
        "European voice in salon and recital settings."
    ),
    "Boston": (
        "Designed by Steinway & Sons and built to exacting standards, Boston pianos bring "
        "Steinway-inspired scale design and materials to a more accessible investment. "
        "They offer rich sustain, responsive touch, and enduring construction for homes, "
        "studios, and teaching rooms that demand serious musical quality."
    ),
    "Essex": (
        "Essex pianos, designed by Steinway & Sons, open the path to the Steinway family of sound "
        "with refined cabinetry and reliable performance. Ideal as a first grand or upright for "
        "aspiring pianists and families, Essex instruments balance value, beauty, and the "
        "musical DNA of their design heritage."
    ),
    "Kurzweil": (
        "Kurzweil digital instruments combine advanced sampling and weighted actions with the "
        "practical advantages of a modern piano — silent practice, versatility, and compact "
        "footprints — without abandoning a convincing acoustic character for home and stage."
    ),
}

# Adapted from official Steinway model pages (eu.steinway.com)
PRODUCT_BLURBS = {
    "Model D-274": (
        "The Steinway D-274 is the flagship concert grand — nearly nine feet in length and regarded "
        "as the unrivaled standard in the world’s most prestigious halls. It offers unsurpassable "
        "volume, colour, and dynamic range, and remains the clear instrument of choice for the "
        "vast majority of performing artists. Powerful and brilliant across all registers, it is "
        "equally at home on the concert stage or in a spacious music salon."
    ),
    "Model C-227": (
        "The Steinway C-227 is the smaller of Steinway’s two concert grands, sharing soundboard and "
        "plate architecture with the Model D. From the most delicate pianissimo to a powerful forte, "
        "it delivers a wide dynamic range and exceptional richness of overtones — ideal for chamber "
        "music, recitals in intimate halls, and generously proportioned living spaces."
    ),
    "Model B-211": (
        "Often called “the perfect grand piano,” the Steinway B-211 is the company’s best-selling "
        "grand. Harmonious in proportion and rich in tone across every register, it flourishes in "
        "private homes, teaching studios, and mid-sized venues. With a length of 211 cm it remains "
        "the most beloved Steinway for dedicated pianists at home — and is available with Spirio."
    ),
    "Model A-188": (
        "The Steinway A-188 is the smaller salon grand — the most curvaceous Steinway — yet surprises "
        "with an extraordinarily rich fullness of tone. Precise and highly responsive, it meets "
        "exacting pianistic demands in private rooms, music schools, and universities, with a "
        "full, impressive bass in a comparatively compact 188 cm footprint."
    ),
    "Model O-180": (
        "The Steinway O-180 is the largest of Steinway’s baby grands and the most popular in that "
        "family. Exceptionally warm and rich for its size, it was designed to bring the full "
        "Steinway sound into more intimate rooms. At 180 cm it is ideal for house concerts, and "
        "it is available with Spirio high-resolution player technology."
    ),
    "Model M-170": (
        "With the Steinway M-170, only the designation is “medium” — its sound is anything but. "
        "This medium baby grand captivates with a responsive, hand-regulated action and "
        "unmistakable Steinway tone, offering greater tonal flexibility than the smaller S-155 "
        "through longer strings and a larger soundboard. A favourite for apartments and smaller homes."
    ),
    "Model S-155": (
        "The Steinway S-155 is the smallest Steinway grand, introduced so that a Steinway could be "
        "heard in virtually any setting. At just 155 cm it fits almost any home, yet it is built "
        "to the same uncompromising standards of touch and tone as larger models — premium "
        "materials and masterful craftsmanship in the most space-efficient grand piano footprint."
    ),
    "Model K-132": (
        "The Steinway K-132 is a tall concert upright — often called the finest upright in the world — "
        "built with the same materials and techniques as Steinway grands. At 132 cm in height it "
        "delivers a vast, resonant voice and a broad tonal spectrum, with Hexagrip pinblock stability "
        "and a diaphragmatic Sitka spruce soundboard suited to homes, studios, and institutions."
    ),
    "Model 4": (
        "Hand-built in Leipzig since 1853, the Blüthner Model 4 grand is celebrated for its golden, "
        "singing tone and Aliquot stringing, which adds a luminous shimmer to the treble. A refined "
        "European voice for salon and recital — warmth, colour, and craftsmanship in a classic "
        "grand footprint."
    ),
}


def main():
    conn = psycopg2.connect(CONN)
    cur = conn.cursor()

    cur.execute("ALTER TABLE businesses ADD COLUMN IF NOT EXISTS blurb text")
    cur.execute("ALTER TABLE brands ADD COLUMN IF NOT EXISTS blurb text")

    cur.execute(
        "UPDATE businesses SET blurb = %s, updated_at = NOW() WHERE slug = %s",
        (BUSINESS_BLURB, "house-of-pianos"),
    )
    print("business updated:", cur.rowcount)

    for name, blurb in BRAND_BLURBS.items():
        cur.execute(
            "UPDATE brands SET blurb = %s, updated_at = NOW() WHERE name = %s",
            (blurb, name),
        )
        print(f"brand {name}: {cur.rowcount}")

    for model, blurb in PRODUCT_BLURBS.items():
        cur.execute(
            "UPDATE products SET blurb = %s, updated_at = NOW() WHERE model = %s",
            (blurb, model),
        )
        print(f"product {model}: {cur.rowcount}")

    conn.commit()
    cur.execute("SELECT name, left(blurb, 50) FROM businesses")
    print("\nbusinesses:", cur.fetchall())
    cur.execute("SELECT name, left(blurb, 40) FROM brands ORDER BY name")
    print("brands:", cur.fetchall())
    cur.close()
    conn.close()
    print("done")


if __name__ == "__main__":
    main()
