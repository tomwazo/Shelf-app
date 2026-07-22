namespace Shelf.Web.Data.Entities;

public class User
{
    public int Id { get; set; }

    public required string Name { get; set; }

    // Easy Auth principal id this row maps to; rows are auto-created on
    // first login (see TECHNICAL_DESIGN.md § User identity).
    public required string ExternalIdentity { get; set; }
}
