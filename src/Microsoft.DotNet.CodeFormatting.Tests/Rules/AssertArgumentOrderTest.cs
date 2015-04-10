using System.Collections.Generic;
using System.Runtime.Remoting;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.DotNet.CodeFormatting.Tests
{
    public class AssertArgumentOrderTest : LocalSemanticRuleTestBase
    {
        internal override ILocalSemanticFormattingRule Rule
        {
            get { return new Rules.AssertArgumentOrderRule(); }
        }

        protected override IEnumerable<MetadataReference> GetSolutionMetadataReferences()
        {
            foreach (var reference in base.GetSolutionMetadataReferences())
            {
                yield return reference;
            }

            yield return MetadataReference.CreateFromAssembly(typeof (Assert).Assembly);
        }

        [Fact]
        public void TestSwapInvertedEqual()
        {
            string source = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.Equal(actual, 1);
    }
}
";
            string expected = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.Equal(1, actual);
    }
}
";

            Verify(source, expected);
        }

        [Fact]
        public void TestSwapInvertedEqualEnum()
        {
            string source = @"
public class Tests
{
    private enum E
    {
        A,
        B,
    }

    public void TestA()
    {
        E actual = E.A;
        Xunit.Assert.Equal(actual, E.A);
    }
}
";
            string expected = @"
public class Tests
{
    private enum E
    {
        A,
        B,
    }

    public void TestA()
    {
        E actual = E.A;
        Xunit.Assert.Equal(E.A, actual);
    }
}
";
            Verify(source, expected);
        }

        [Fact]
        public void TestSwapInvertedEqualConstField()
        {
            string source = @"
public class Tests
{
    private const int A;

    public void TestA()
    {
        int actual = A;
        Xunit.Assert.Equal(actual, A);
    }
}
";
            string expected = @"
public class Tests
{
    private const int A;

    public void TestA()
    {
        int actual = A;
        Xunit.Assert.Equal(A, actual);
    }
}
";
            Verify(source, expected);
        }

        [Fact]
        public void TestSwapInvertedNotEqual()
        {
            string source = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.NotEqual(actual, 1);
    }
}
";
            string expected = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.NotEqual(1, actual);
    }
}
";
            Verify(source, expected);
        }

        [Fact]
        public void TestSwapInvertedEqualFromUsing()
        {
            string source = @"
using Xunit;

public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Assert.Equal(actual, 1);
    }
}
";
            string expected = @"
using Xunit;

public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Assert.Equal(1, actual);
    }
}
";
            Verify(source, expected);
        }

        [Fact]
        public void TestIgnoredCorrectEqual()
        {
            string text = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.Equal(1, actual);
    }
}
";
            Verify(text, text);
        }

        [Fact]
        public void TestIgnoredDoubleConstEqual()
        {
            string text = @"
public class Tests
{
    public void TestA()
    {
        Xunit.Assert.Equal(1, 2);
    }
}
";
            Verify(text, text);
        }

        [Fact]
        public void TestIgnoredDoubleVariableEqual()
        {
            string text = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        int expected = 1;
        Xunit.Assert.Equal(actual, expected);
    }
}
";
            Verify(text, text);
        }

        [Fact]
        public void TestIgnoredCorrectNotEqual()
        {
            string text = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.NotEqual(1, actual);
    }
}
";
            Verify(text, text);
        }

        [Fact]
        public void TestIgnoreOtherAssert()
        {
            string text = @"
public class Assert
{
    public void Equal(int expected, int actual)
    {
    }
}

public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Assert.NotEqual(1, actual);
    }
}
";
            Verify(text, text);
        }
    }
}
