using System;
using System.Collections.Generic;

namespace AutoCAD.Gateway.Tests;

internal static class TestAssert
{
    public static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}. Expected '{expected}', got '{actual}'.");
        }
    }

    public static T AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}: {message}");
    }
}
