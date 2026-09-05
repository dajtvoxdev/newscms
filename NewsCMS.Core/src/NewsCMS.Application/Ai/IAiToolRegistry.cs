namespace NewsCMS.Application.Ai;

public interface IAiToolRegistry
{
    IReadOnlyList<IAiTool> All();
    IAiTool? Find(string key);
}
