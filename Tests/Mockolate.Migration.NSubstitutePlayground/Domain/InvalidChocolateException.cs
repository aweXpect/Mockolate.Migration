namespace Mockolate.Migration.NSubstitutePlayground.Domain;

/// <summary>Domain exception thrown for invalid chocolate operations.</summary>
public class InvalidChocolateException : Exception
{
	public InvalidChocolateException() { }
	public InvalidChocolateException(string message) : base(message) { }
}
