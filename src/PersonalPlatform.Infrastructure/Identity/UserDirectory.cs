using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Profile;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.Infrastructure.Identity;

public sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<string> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException();
}
