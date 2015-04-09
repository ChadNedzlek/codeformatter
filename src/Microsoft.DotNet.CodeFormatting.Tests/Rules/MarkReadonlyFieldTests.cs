using System;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.DotNet.CodeFormatting.Tests
{
    /// <summary>
    /// Test that fields are correctly identified as readonly.
    /// </summary>
    public class MarkReadonlyFieldTests : GlobalSemanticRuleTestBase
    {
        internal override IGlobalSemanticFormattingRule Rule
        {
            get { return new Rules.MarkReadonlyFieldsRule(); }
        }

        // In general a single sting with "READONLY" in it is used
        // for the tests to simplify the before/after comparison
        // The Original method will remove it, and the Readonly will replace it
        // with the keyword

        [Fact]
        public void TestIgnoreExistingReadonlyField()
        {
            var text = @"
class C
{
    private readonly int ignored;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyWithNoReferences()
        {
            var text = @"
class C
{
    private READONLY int changed;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyInternalWithNoReferences()
        {
            var text = @"
class C
{
    internal READONLY int changed;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyInternalWithNoReferencesByInternalsVisibleTo()
        {
            var text = @"
[assembly:System.Runtime.CompilerServices.InternalsVisibleToAttribute(""Some.Other.Assembly"")]
class C
{
    internal int changed;
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithWriteReferences()
        {
            var text = @"
class C
{
    private int ignored;

    public void T()
    {
        ignored = 5;
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyWithReadReferences()
        {
            var text = @"
class C
{
    private READONLY int changed;
    private int writen;

    public void T()
    {
        int x = change;
        x = changed;
        writen = changed;
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithRefArgument()
        {
            var text = @"
class C
{
    private int changed;

    public void M(ref int a)
    {
    }

    public void T()
    {
        M(ref changed);
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestIgnoredReadonlyWithOutArgument()
        {
            var text = @"
class C
{
    private int changed;

    public void N(out int a)
    {
    }

    public void T()
    {
        N(out changed);
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        [Fact]
        public void TestMarkReadonlyWithWriteReferencesInConstructor()
        {
            var text = @"
class C
{
    private READONLY int changed;

    public C()
    {
        changed = 5;
        M(ref changed);
        N(out changed);
    }

    public void M(ref int a)
    {
    }

    public void N(out int a)
    {
    }
}
";
            Verify(Original(text), Readonly(text));
        }

        private string Original(string text)
        {
            return text.Replace("READONLY ", "");
        }

        private string Readonly(string text)
        {
            return text.Replace("READONLY ", "readonly ");
        }
    }
}