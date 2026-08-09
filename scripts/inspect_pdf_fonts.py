from pypdf import PdfReader

path = r"c:\Users\ACER\Desktop\inter\House of Pianos Piano Proposal Template.pdf"
r = PdfReader(path)
fonts = set()
for i, p in enumerate(r.pages):
    resources = p.get("/Resources")
    if not resources:
        continue
    font_dict = resources.get("/Font")
    if not font_dict:
        continue
    for name, font in font_dict.items():
        f = font.get_object()
        base = f.get("/BaseFont", "?")
        fonts.add(str(base))
        print(f"p{i+1} {name}: BaseFont={base} Subtype={f.get('/Subtype')} Encoding={f.get('/Encoding')}")

print("\nUNIQUE:")
for f in sorted(fonts):
    print(" ", f)
