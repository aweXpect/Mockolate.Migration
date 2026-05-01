namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>Concrete recipe — used for partial mocks (NSubstitute <c>ForPartsOf</c>).</summary>
public class ChocolateRecipe
{
	public virtual string Name { get; set; } = "Truffle";
	public virtual int CocoaPercent { get; set; } = 70;

	public virtual ChocolateBar Bake(int amount) =>
		new(Name, CocoaPercent, amount * 1.5m);

	public virtual bool Validate() => !string.IsNullOrEmpty(Name);

	public virtual void Reset()
	{
		Name = "Truffle";
		CocoaPercent = 70;
	}
}
