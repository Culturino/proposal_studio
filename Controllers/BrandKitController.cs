using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/brand-kit")]
    [Authorize]
    public class BrandKitController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BrandStyleService _brand;
        private readonly AuditService _audit;

        public BrandKitController(AppDbContext context, BrandStyleService brand, AuditService audit)
        {
            _context = context;
            _brand = brand;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] Guid? businessId = null)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId(businessId);
            if (houseId is not Guid id)
                return BadRequest(BusinessScope.MissingMessage);

            var kit = await _brand.GetKitAsync(id);
            var palette = kit == null ? PdfPalette.Defaults : PdfPalette.FromJson(kit.Colors);
            var logoFile = kit?.LogoFile ?? BrandStyleService.DefaultLogoFile;
            var template = await _context.Templates
                .Where(t => t.BusinessId == id && t.Active)
                .OrderByDescending(t => t.Version)
                .Select(t => new { t.Id, t.Key, t.Name, t.Version })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                colors = palette.ToMap(),
                logoFile,
                logoUrl = BrandStyleService.PublicLogoPath(logoFile),
                template
            });
        }

        [HttpPatch]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(
            [FromBody] UpdateBrandKitRequest request,
            [FromQuery] Guid? businessId = null)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId(businessId);
            if (houseId is not Guid id)
                return BadRequest(BusinessScope.MissingMessage);

            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == id);
            if (business == null)
                return BadRequest(BusinessScope.MissingMessage);

            var incoming = request.Colors ?? new Dictionary<string, string>();
            foreach (var pair in incoming)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                    continue;
                if (!PdfPalette.IsHex(pair.Value))
                    return BadRequest($"“{pair.Key}” is not a hex color.");
            }

            var kit = await _brand.GetKitAsync(business.Id);
            var before = (kit == null ? PdfPalette.Defaults : PdfPalette.FromJson(kit.Colors)).ToMap();
            var merged = new Dictionary<string, string>(before, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in incoming)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    merged[pair.Key] = pair.Value;
            }

            var (palette, version) = await _brand.SaveAsync(id, merged);
            await _audit.LogAsync(User, "update", "brand_kit", business.BrandKitId, before, palette.ToMap());

            var template = await _context.Templates
                .Where(t => t.BusinessId == id && t.Active)
                .OrderByDescending(t => t.Version)
                .Select(t => new { t.Id, t.Key, t.Name, t.Version })
                .FirstOrDefaultAsync();

            var saved = await _brand.GetKitAsync(id);
            var logoFile = saved?.LogoFile ?? BrandStyleService.DefaultLogoFile;

            return Ok(new
            {
                colors = palette.ToMap(),
                logoFile,
                logoUrl = BrandStyleService.PublicLogoPath(logoFile),
                template,
                templateVersion = version
            });
        }

        [HttpPost("logo")]
        [Authorize(Roles = "Admin")]
        [RequestSizeLimit(BrandStyleService.MaxLogoBytes)]
        public async Task<IActionResult> UploadLogo(IFormFile file, [FromQuery] Guid? businessId = null)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId(businessId);
            if (houseId is not Guid id)
                return BadRequest(BusinessScope.MissingMessage);

            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == id);
            if (business == null)
                return BadRequest(BusinessScope.MissingMessage);

            if (file == null || file.Length == 0)
                return BadRequest("No file was uploaded.");
            if (file.Length > BrandStyleService.MaxLogoBytes)
                return BadRequest("Logo must be 8 MB or smaller.");

            try
            {
                await using var stream = file.OpenReadStream();
                var (fileName, url, version) = await _brand.SaveLogoAsync(id, stream);
                await _audit.LogAsync(User, "update", "brand_kit_logo", business.BrandKitId, null, new { fileName, version });

                return Ok(new
                {
                    logoFile = fileName,
                    logoUrl = url,
                    templateVersion = version
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    public class UpdateBrandKitRequest
    {
        public Dictionary<string, string>? Colors { get; set; }
    }
}
