using System.ComponentModel.DataAnnotations;
using PoWorks_Rework.Controllers;
using Xunit;

namespace PoWorks_Rework.Tests;

public class UserModelValidationTests
{
    [Fact]
    public void CreateUser_RequiresUsernameAndPassword()
    {
        var model = new CreateUserViewModel
        {
            Username = "",
            Password = "",
            UserType = "Management"
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateUserViewModel.Username)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateUserViewModel.Password)));
    }

    [Fact]
    public void CreateUser_WithRequiredFields_IsDataAnnotationValid()
    {
        var model = new CreateUserViewModel
        {
            Username = "manager1",
            Password = "ValidPassword!2026",
            UserType = "Management"
        };

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void EditUser_RequiresUserType()
    {
        var model = new EditUserViewModel
        {
            Id = "1",
            Username = "manager1",
            UserType = ""
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EditUserViewModel.UserType)));
    }

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
