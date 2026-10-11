using PersonalPlatform.Contracts.Profile;

namespace PersonalPlatform.Client.Services;

/// <summary>The signed-in user's preferences, loaded from the API after sign-in and cleared on sign-out.</summary>
public sealed class UserContext(ApiClient api, Loc loc)
{
    public ProfileDto? Profile { get; private set; }

    public ReferenceDataDto? Reference { get; private set; }

    public event Action? Changed;

    public string Currency => Profile?.CurrencyCode ?? "RUB";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Reference ??= await api.GetAsync<ReferenceDataDto>("/api/v1/profile/reference-data", cancellationToken);
        Set(await api.GetAsync<ProfileDto>("/api/v1/profile", cancellationToken));
    }

    public void Set(ProfileDto profile)
    {
        Profile = profile;
        loc.Apply(profile, Reference?.Currencies);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Profile = null;
        Changed?.Invoke();
    }
}
