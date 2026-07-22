namespace Shelf.Web.Data.Entities;

public class Genre
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public List<Item> Items { get; set; } = [];
}
