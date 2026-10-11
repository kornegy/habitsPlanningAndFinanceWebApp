using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Profile;
using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/profile")]
public sealed class ProfileController(ProfileService profiles) : ControllerBase
{
    [HttpGet]
    public Task<ProfileDto> Get(CancellationToken cancellationToken) => profiles.GetAsync(cancellationToken);

    [HttpPut]
    public Task<ProfileDto> Update(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(request, cancellationToken);

    /// <summary>Supported currencies, time zones and locales (public, non-personal reference data).</summary>
    [HttpGet("reference-data")]
    [AllowAnonymous]
    public ReferenceDataDto ReferenceData() => ProfileService.GetReferenceData();
}
