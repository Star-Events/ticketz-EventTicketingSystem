using System.ComponentModel.DataAnnotations;

namespace EventTicketingSystem.Models
{
    // This is the shape of data the Register form will post.
    public class RegisterVm
    {
        [Required, Display(Name = "Full name")]
        [StringLength(100, MinimumLength = 2)]
        public string FullName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Phone, Display(Name = "Phone (optional)")]
        public string? PhoneNumber { get; set; }

        [Required, DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password), Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";

        // Option B: let user choose Customer or Organizer (admin is not self-registerable)
        [Required, Display(Name = "Register as")]
        [RegularExpression("^(Customer|Organizer)$", ErrorMessage = "Please select Customer or Organizer.")]
        public string Role { get; set; } = "Customer";

        [Display(Name = "I agree to the Terms & Privacy")]
        public bool AcceptTerms { get; set; } = true; // keep true for demo; enforce later if you want
    }
}
