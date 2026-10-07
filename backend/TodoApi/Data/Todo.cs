using System.ComponentModel.DataAnnotations;

namespace TodoApi.Data;

public class Todo
{
    public const int TitleMaxLength = 200;

    public int Id { get; set; }

    [Required]
    [MaxLength(TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTime CreatedAt { get; set; }
}
