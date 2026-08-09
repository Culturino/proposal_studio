import psycopg2

conn = psycopg2.connect(
    "host=localhost port=5432 dbname=proposal_studio user=postgres password=ilovesatan"
)
cur = conn.cursor()

cur.execute(
    """
    SELECT column_name
    FROM information_schema.columns
    WHERE table_name = 'products' AND column_name = 'blurb'
    """
)
print("column:", cur.fetchall())

cur.execute(
    """
    SELECT model, status,
           CASE
             WHEN blurb IS NULL OR btrim(blurb) = '' THEN '(empty)'
             ELSE left(blurb, 80)
           END AS blurb_preview
    FROM products
    ORDER BY model
    """
)
print("\nproducts:")
for row in cur.fetchall():
    print(f"  {row[0]} [{row[1]}]: {row[2]}")

cur.close()
conn.close()
