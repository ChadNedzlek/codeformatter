// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

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
            foreach (MetadataReference reference in base.GetSolutionMetadataReferences())
            {
                yield return reference;
            }

            yield return MetadataReference.CreateFromAssembly(typeof (Assert).Assembly);
        }

        [Fact]
        public void TestSwapInvertedEqual()
        {
            var source = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.Equal(actual, 1);
    }
}
";
            var expected = @"
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
            var source = @"
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
            var expected = @"
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
            var source = @"
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
            var expected = @"
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
            var source = @"
public class Tests
{
    public void TestA()
    {
        int actual = 1;
        Xunit.Assert.NotEqual(actual, 1);
    }
}
";
            var expected = @"
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
            var source = @"
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
            var expected = @"
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
            var text = @"
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
            var text = @"
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
            var text = @"
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
            var text = @"
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
            var text = @"
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
