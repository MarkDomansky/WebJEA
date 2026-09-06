namespace WebJEA;

public class MenuItem
{
    public string ID { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Synopsis { get; set; }

    public string Uri()
    {
        return "command.html?cmdid=" + ID;
    }
}
