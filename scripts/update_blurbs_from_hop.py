"""
Update products.blurb from houseofpiano.com-sourced copy for matching catalog models.
"""
import psycopg2

CONN = "host=localhost port=5432 dbname=proposal_studio user=postgres password=ilovesatan"

# Sourced from https://www.houseofpiano.com/ product pages (matched by model).
# Models with no listing on the site are left unchanged.
BLURBS = {
    "Model D-274": (
        "The Steinway Model D-274 is the pinnacle of the Steinway line—the uncompromising "
        "full concert grand, measuring nearly nine feet in length. Universally recognized as "
        "the standard against which all other concert instruments are judged, it projects both "
        "thunderous power and the most delicate pianissimo."
    ),
    "Model B-211": (
        "This magnificent 211 cm grand is often referred to as “the perfect piano.” "
        "Wonderfully balanced and versatile, it performs extremely well in intimate settings, "
        "teaching studios, and mid-sized venues—with a rich tone palette ideal for private "
        "rooms and studios."
    ),
    "Model A-188": (
        "The legendary Model A—rich bass in a footprint made for the home. It produces a "
        "powerful, warm sound; the solid spruce soundboard vibrates freely and efficiently. "
        "Large enough for those who demand a full rich bass, yet small enough for almost any residence."
    ),
    "Model O-180": (
        "The Model O is the largest of Steinway’s baby grands—exceptionally warm and rich for "
        "its size, with limitless musical expression. A source of joy since the early 1900s, "
        "and available with Spirio high-resolution player technology."
    ),
    "Model M-170": (
        "The Model M is a medium-size grand that is the perfect instrument for the home. "
        "Sensitive mechanics and the unmistakable Steinway sound—called a medium grand, but "
        "there is nothing medium about its tone."
    ),
    "Model S-155": (
        "The Steinway Model S-155 is the smallest member of the grand family, meticulously "
        "scaled and voiced to bring the legendary Steinway sound into the most confined spaces. "
        "A triumph of engineering that maximizes tonal depth within a minimal footprint."
    ),
}

TAGLINES = {
    "Model D-274": "The concert standard",
    "Model B-211": "“The perfect piano”",
    "Model A-188": "The salon grand",
    "Model O-180": "The living-room grand",
    "Model M-170": "The studio grand",
    "Model S-155": "The baby grand",
}


def main():
    conn = psycopg2.connect(CONN)
    cur = conn.cursor()
    cur.execute("SELECT model, LEFT(blurb, 60) FROM products ORDER BY model")
    print("Before:")
    for row in cur.fetchall():
        print(f"  {row[0]}: {row[1]!r}")

    updated = 0
    for model, blurb in BLURBS.items():
        cur.execute(
            """
            UPDATE products
            SET blurb = %s,
                tagline = COALESCE(%s, tagline),
                updated_at = NOW()
            WHERE model = %s
              AND COALESCE(status, 'active') NOT IN ('archived', 'deleted')
            """,
            (blurb, TAGLINES.get(model), model),
        )
        updated += cur.rowcount

    conn.commit()
    print(f"\nUpdated {updated} row(s).\nAfter:")
    cur.execute(
        "SELECT model, LEFT(blurb, 90) FROM products WHERE model = ANY(%s) ORDER BY model",
        (list(BLURBS.keys()),),
    )
    for row in cur.fetchall():
        print(f"  {row[0]}: {row[1]!r}")
    cur.close()
    conn.close()


if __name__ == "__main__":
    main()
