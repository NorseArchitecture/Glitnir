using Shouldly;
using Voyage.Extensions.AI;

namespace Voyage.Extensions.AI.Tests;

public class VoyageEmbeddingGeneratorOptionsTests
{
	static VoyageEmbeddingGeneratorOptions Valid() => new()
	{
		Model = "voyage-4",
		InputType = VoyageInputType.Document,
		ApiKey = "test-key",
	};

	[Fact]
	public void Validate_passes_for_valid_options() =>
		Should.NotThrow(() => Valid().Validate());

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Validate_throws_when_model_missing(string model)
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { Model = model };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("Model");
	}

	[Fact]
	public void Validate_throws_when_input_type_unspecified()
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { InputType = VoyageInputType.Unspecified };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("InputType");
	}

	[Fact]
	public void Validate_throws_when_api_key_missing()
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { ApiKey = "" };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("ApiKey");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1001)]
	public void Validate_throws_when_max_batch_size_out_of_range(int size)
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { MaxBatchSize = size };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("MaxBatchSize");
	}

	[Fact]
	public void Validate_throws_when_output_dimension_not_positive()
	{
		VoyageEmbeddingGeneratorOptions options = Valid() with { OutputDimension = 0 };
		Should.Throw<InvalidOperationException>(() => options.Validate())
			.Message.ShouldContain("OutputDimension");
	}

	[Fact]
	public void Truncation_defaults_to_false() =>
		Valid().Truncation.ShouldBeFalse();

	[Fact]
	public void BaseUrl_defaults_to_voyage_api() =>
		Valid().BaseUrl.ShouldBe(new Uri("https://api.voyageai.com/v1/"));
}
