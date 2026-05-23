using Microsoft.AspNetCore.Identity;

namespace BettingApp.Data
{
    public class CustomIdentityErrorDescriber : IdentityErrorDescriber
    {
        public override IdentityError PasswordRequiresNonAlphanumeric()
        {
            return new IdentityError
            {
                Code = nameof(PasswordRequiresNonAlphanumeric),
                Description = "Passwords must have at least one non-alphanumeric character (e.g., ! @ # $ % ^ & * ?)."
            };
        }
    }
}
