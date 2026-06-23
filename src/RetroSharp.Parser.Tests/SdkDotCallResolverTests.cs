namespace RetroSharp.Parser.Tests;

public class SdkDotCallResolverTests
{
    [Fact]
    public void Receiver_in_scope_shadows_a_known_sdk_module()
    {
        SdkDotCallResolver.Resolve(isKnownSdkModule: true, hasReceiver: true)
            .Should().Be(SdkDotCallKind.Receiver);
    }

    [Fact]
    public void Known_module_without_a_receiver_resolves_as_sdk_call()
    {
        SdkDotCallResolver.Resolve(isKnownSdkModule: true, hasReceiver: false)
            .Should().Be(SdkDotCallKind.SdkModule);
    }

    [Fact]
    public void Receiver_resolves_when_the_name_is_not_a_known_module()
    {
        SdkDotCallResolver.Resolve(isKnownSdkModule: false, hasReceiver: true)
            .Should().Be(SdkDotCallKind.Receiver);
    }

    [Fact]
    public void Neither_a_module_nor_a_receiver_is_unknown()
    {
        SdkDotCallResolver.Resolve(isKnownSdkModule: false, hasReceiver: false)
            .Should().Be(SdkDotCallKind.Unknown);
    }
}
