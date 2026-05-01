namespace Mockolate.Migration.MoqPlayground.Domain;

/// <summary>Concrete recipe — used for partial mocks (Moq <c>CallBase</c>, NSubstitute <c>ForPartsOf</c>).</summary>
public class ChocolateRecipe
{
	public virtual string Name { get; set; } = "Truffle";
	public virtual int CocoaPercent { get; set; } = 70;

	public virtual ChocolateBar Bake(int amount) =>
		new(Name, CocoaPercent, amount * 1.5m);

	public virtual bool Validate() => !string.IsNullOrEmpty(Name);

	/// <summary>Used for Moq <c>Protected()</c>.</summary>
	protected virtual int InternalSecret() => 42;

	public int CallInternalSecret() => InternalSecret();
}
