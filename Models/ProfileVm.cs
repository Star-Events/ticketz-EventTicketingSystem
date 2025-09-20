using System.ComponentModel.DataAnnotations;

namespace EventTicketingSystem.Models
{
    public class ProfileVm
    {
        [Display(Name = "Email")]
        public string Email { get; set; } = "";   // read-only in UI

        [Required, Display(Name = "Full name")]
        [StringLength(200)]
        public string FullName { get; set; } = "";

        // Change password (optional)
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string? CurrentPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        [MinLength(6, ErrorMessage = "New password must be at least 6 characters.")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string? ConfirmNewPassword { get; set; }
    }
}
