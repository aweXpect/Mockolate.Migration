namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>Used to exercise async, generics on parameters, collection parameters.</summary>
public interface IChocolateFactory
{
	int Capacity { get; }
	Task<ChocolateBar> BakeAsync(string recipe, int cocoa);
	Task<IReadOnlyList<ChocolateBar>> BatchBakeAsync(IEnumerable<string> recipes);
	bool RegisterRecipe(string name);
}
