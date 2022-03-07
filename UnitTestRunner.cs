#if DEBUG
// #define DEBUG__RETHROW_ON_FAILED_TEST
#endif

#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Linq;
using System.IO;
using System;

namespace Unknown6656.Testing;

using static Console;


public abstract class UnitTestRunner
{
    public virtual void Test_StaticInit()
    {
    }

    public virtual void Test_StaticCleanup()
    {
    }

    [TestInitialize]
    public virtual void Test_Init()
    {
    }

    [TestCleanup]
    public virtual void Test_Cleanup()
    {
    }

    /// <summary>
    /// Skips the (current) test method.
    /// This function is intended to be called from inside a tested method (i.e. a method marked with the [<see cref="TestMethodAttribute"/>]-attribute).
    /// This function does never return, instead it will always throw a <see cref="SkippedException"/>.
    /// </summary>
    /// <exception cref="SkippedException">Always thrown.</exception>
    [DoesNotReturn]
    public static void Skip() => throw new SkippedException();


    // prevent override
    public sealed override bool Equals(object obj) => base.Equals(obj);

    // prevent override
    public sealed override int GetHashCode() => base.GetHashCode();

    // prevent override
    public sealed override string ToString() => base.ToString();



    private static void AddTime(ref long target, Stopwatch sw)
    {
        sw.Stop();
        target += sw.ElapsedTicks;
        sw.Restart();
    }

    private static void Print(string text, ConsoleColor color)
    {
        ForegroundColor = color;
        Write(text);
    }

    private static void PrintLine(string text, ConsoleColor color) => Print(text + '\n', color);

    private static void PrintHeader(string text, int width)
    {
        int line_width = width - text.Length - 2;
        string line = new('═', line_width / 2);

        WriteLine($"{line} {text} {line}{(line_width % 2 == 0 ? "" : "═")}");
    }

    internal static void PrintGraph(int padding, int width, string description, params (double value, ConsoleColor color)[] values)
    {
        List<(double value, ConsoleColor color)> adjusted;
        double sum = values.Sum(t => t.value);

        width -= 2;
        adjusted = values.Select(v => (v.value / sum * width is double d && double.IsFinite(d) ? Math.Max(0, d) : 0, v.color)).ToList();

        double remainder = Math.Max(0, width - adjusted.Sum(t => t.value));
        double max_value = double.MinValue;
        int max_index = -1;

        for (int i = 0; i < adjusted.Count; ++i)
            if (adjusted[i].value > max_value)
                (max_index, max_value) = (i, adjusted[i].value);

        adjusted[max_index] = (adjusted[max_index].value + remainder, adjusted[max_index].color);

        Print($"{new string(' ', padding)}│", ConsoleColor.White);

        ConsoleColor background = BackgroundColor;
        int barwidth = 0;
        bool half = false;

        for (int i = 0; i < adjusted.Count; i++)
        {
            Print(new string('█', (int)adjusted[i].value), adjusted[i].color);

            barwidth += (int)adjusted[i].value;

            if ((adjusted[i].value % 1) >= .5)
                if (half ^= true)
                {
                    ForegroundColor = adjusted[i].color;

                    if (i < adjusted.Count - 1 && adjusted[i + 1].value >= .5)
                        BackgroundColor = adjusted[i + 1].color;

                    Write('▌');

                    BackgroundColor = background;
                    ++barwidth;
                }
        }

        if (barwidth < width)
            Write(new string(width - barwidth > 1 ? '▓' : '█', width - barwidth));

        PrintLine($"│ {description ?? ""}", ConsoleColor.White);
    }


    /// <summary>
    /// Runs all unit tests in the assembly which called this method.
    /// </summary>
    /// <returns>The number of failed unit tests (or <pre>-1</pre> if a generic exception occurred).</returns>
    public static int RunTests() => RunTests(new[] { Assembly.GetCallingAssembly() });

    /// <summary>
    /// Runs all unit tests in all assemblies loaded by the given <see cref="AppDomain"/>(s).
    /// </summary>
    /// <param name="domains">The app domains to be unit tested.</param>
    /// <returns>The number of failed unit tests (or <pre>-1</pre> if a generic exception occurred).</returns>
    public static int RunTests(params AppDomain[] domains!!) => RunTests(from d in domains
                                                                         from asm in d.GetAssemblies()
                                                                         select asm);

    /// <summary>
    /// Runs all unit tests in all given assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to be unit tested.</param>
    /// <returns>The number of failed unit tests (or <pre>-1</pre> if a generic exception occurred).</returns>
    public static int RunTests(params Assembly[] assemblies!!) => RunTests(assemblies as IEnumerable<Assembly>);

    /// <summary>
    /// Runs all unit tests in all given assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to be unit tested.</param>
    /// <returns>The number of failed unit tests (or <pre>-1</pre> if a generic exception occurred).</returns>
    public static int RunTests(IEnumerable<Assembly> assemblies!!)
    {
        const int WIDTH = 110;
        int exitcode = 0;
        ConsoleColor foreground = ForegroundColor;
        Encoding encoding = OutputEncoding;

        ForegroundColor = ConsoleColor.White;
        OutputEncoding = Encoding.Default;

        try
        {
            #region REFLECTION + INVOCATION

            Stopwatch sw = new();
            List<MethodRunResults> partial_results = new();
            int passed = 0, failed = 0, skipped = 0;
            long sw_sinit, sw_init, sw_method;

            Type[] types = (from asm in assemblies
                            from type in asm.GetTypes()
                            let attr = type.GetCustomAttributes<TestClassAttribute>(true).FirstOrDefault()
                            where attr is { }
                            orderby type.Name ascending
                            orderby type.GetCustomAttributes<PriorityAttribute>(true).FirstOrDefault()?.Priority ?? 0 descending
                            select type).ToArray();

            PrintHeader("UNIT TESTS", WIDTH);
            WriteLine($@"
Testing {types.Length} type(s):
{string.Concat(types.Select(t => $"  [{new FileInfo(t.Assembly.Location).Name}] {t.FullName}\n"))}");

            foreach (Type t in types)
            {
                sw.Restart();
                sw_sinit = sw_init = sw_method = 0;

                bool skipclass = t.GetCustomAttributes<SkipAttribute>(true).FirstOrDefault() != null;
                dynamic? container = skipclass ? null : Activator.CreateInstance(t);
                MethodInfo? sinit = t.GetMethod(nameof(Test_StaticInit));
                MethodInfo? scleanup = t.GetMethod(nameof(Test_StaticInit));
                MethodInfo? init = t.GetMethod(nameof(Test_Init));
                MethodInfo? cleanup = t.GetMethod(nameof(Test_Cleanup));
                int tpassed = 0, tfailed = 0, tskipped = 0, pleft, ptop, rptop;

                WriteLine($"  Testing class '{t.FullName}'");

                sinit?.Invoke(container, Array.Empty<object>());

                AddTime(ref sw_sinit, sw);

                IEnumerable<(MethodInfo method, object[] args)> get_methods()
                {
                    foreach (MethodInfo nfo in t.GetMethods().OrderBy(m => m.Name))
                        if (nfo.GetCustomAttributes<TestMethodAttribute>()?.FirstOrDefault() is { })
                        {
                            TestWithAttribute[] attr = nfo.GetCustomAttributes<TestWithAttribute>()?.ToArray() ?? Array.Empty<TestWithAttribute>();

                            if (attr.Length > 0)
                                foreach (TestWithAttribute tw in attr)
                                    if (nfo.ContainsGenericParameters)
                                    {
                                        ParameterInfo[] pars = nfo.GetParameters();
                                        List<Type> types = new();

                                        for (int i = 0; i < pars.Length; ++i)
                                            if (pars[i].ParameterType.IsGenericParameter)
                                                types.Add(tw.Arguments[i].GetType());

                                        MethodInfo concrete = nfo.MakeGenericMethod(types.ToArray());

                                        yield return (concrete, tw.Arguments);
                                    }
                                    else
                                        yield return (nfo, tw.Arguments);
                            else
                                yield return (nfo, Array.Empty<object>());
                        }
                }

                foreach ((MethodInfo nfo, object[] args) in get_methods())
                {
                    Write("    [");

                    ptop = CursorTop;
                    pleft = CursorLeft;

                    Write($"    ] Testing '{nfo.Name}({string.Join(", ", nfo.GetParameters().Select(p => p.ParameterType.FullName))})'");

                    if (args.Length > 0)
                        Write($"with ({string.Join(", ", args)})");

                    rptop = CursorTop;

                    void WriteResult(ConsoleColor clr, string text)
                    {
                        int ttop = CursorTop;

                        ForegroundColor = clr;
                        CursorLeft = pleft;
                        CursorTop = ptop;

                        WriteLine(text);

                        ForegroundColor = ConsoleColor.White;
                        CursorTop = rptop + 1;
                    }

                    try
                    {
                        if ((nfo.GetCustomAttributes<SkipAttribute>().FirstOrDefault() != null) || skipclass)
                            Skip();

                        init?.Invoke(container, Array.Empty<object>());

                        AddTime(ref sw_init, sw);

                        nfo.Invoke(container, args);

                        AddTime(ref sw_method, sw);

                        cleanup?.Invoke(container, Array.Empty<object>());

                        AddTime(ref sw_init, sw);

                        WriteResult(ConsoleColor.Green, "PASS");

                        ++passed;
                        ++tpassed;
                    }
                    catch (Exception ex)
                    when ((ex is SkippedException) || (ex?.InnerException is SkippedException))
                    {
                        WriteResult(ConsoleColor.Yellow, "SKIP");

                        ++skipped;
                        ++tskipped;
                    }
                    catch (Exception ex)
                    {
#if DEBUG__RETHROW_ON_FAILED_TEST
                        if (ex is TargetInvocationException { InnerException: { } inner })
                        {
                            ExceptionDispatchInfo.Capture(inner).Throw();

                            throw;
                        }
#endif
                        WriteResult(ConsoleColor.Red, "FAIL");

                        ++failed;
                        ++tfailed;

                        if (ex is AssertFailedException fail && string.IsNullOrWhiteSpace(fail.Message))
                            continue;

                        ForegroundColor = ConsoleColor.Red;

                        while (ex?.InnerException is { })
                        {
                            ex = ex.InnerException;

                            WriteLine($"       [{ex.GetType()}] {ex.Message}\n{string.Join("\n", ex.StackTrace?.Split('\n').Select(x => $"     {x}") ?? Array.Empty<string>())}");
                        }

                        ForegroundColor = ConsoleColor.White;
                    }

                    AddTime(ref sw_method, sw);
                }

                scleanup?.Invoke(container, Array.Empty<object>());

                AddTime(ref sw_sinit, sw);

                partial_results.Add(new(t, tpassed, tskipped, tfailed, sw_sinit, sw_init, sw_method));
            }

            #endregion
            #region PRINT RESULTS

            int total = passed + failed + skipped;
            double time = partial_results.Select(r => r.TimeCtor + r.TimeInit + r.TimeMethod).Sum();
            double pr = total == 0 ? 0 : passed / (double)total;
            double sr = total == 0 ? 0 : skipped / (double)total;
            double tr;
            const int i_wdh = WIDTH - 35;

            WriteLine();
            PrintHeader("TEST RESULTS", WIDTH);
            WriteLine();

            PrintGraph(0, WIDTH, "", (pr, ConsoleColor.Green), (sr, ConsoleColor.Yellow), (total == 0 ? 0 : 1 - pr - sr, ConsoleColor.Red));
            Print($@"
    MODULES: {partial_results.Count,3}
    METHODS: {passed + failed + skipped,3}
    PASSED:  {passed,3} ({pr * 100,7:F3} %)
    SKIPPED: {skipped,3} ({sr * 100,7:F3} %)
    FAILED:  {failed,3} ({(total == 0 ? 0 : 1 - pr - sr) * 100,7:F3} %)
    TIME:    {time * 1000d / Stopwatch.Frequency,9:F3} ms
    DETAILS:", ConsoleColor.White);

            foreach (MethodRunResults res in partial_results)
            {
                double mtime = res.TimeCtor + res.TimeInit + res.TimeMethod;
                double tot = res.Passed + res.Failed + res.Skipped;

                pr = tot == 0 ? 0 : res.Passed / tot;
                sr = tot == 0 ? 0 : res.Failed / tot;
                tr = time == 0 ? 0 : mtime / time;

                double tdt_ct = mtime < double.Epsilon ? 0 : res.TimeCtor / mtime;
                double tdt_in = mtime < double.Epsilon ? 0 : res.TimeInit / mtime;
                double tdt_tt = mtime < double.Epsilon ? 0 : res.TimeMethod / mtime;

                WriteLine($@"
        MODULE:  {res.Type.AssemblyQualifiedName}
        PASSED:  {res.Passed,3} ({pr * 100,7:F3} %)
        SKIPPED: {res.Failed,3} ({sr * 100,7:F3} %)
        FAILED:  {res.Skipped,3} ({(tot == 0 ? 0 : 1 - pr - sr) * 100,7:F3} %)
        TIME:    {mtime * 1000d / Stopwatch.Frequency,9:F3} ms ({tr * 100d,7:F3} %)
            CONSTRUCTORS AND DESTRUCTORS: {res.TimeCtor * 1000d / Stopwatch.Frequency,9:F3} ms ({tdt_ct * 100d,7:F3} %)
            INITIALIZATION AND CLEANUP:   {res.TimeInit * 1000d / Stopwatch.Frequency,9:F3} ms ({tdt_in * 100d,7:F3} %)
            METHOD TEST RUNS:             {res.TimeMethod * 1000d / Stopwatch.Frequency,9:F3} ms ({tdt_tt * 100d,7:F3} %)");
                PrintGraph(8, i_wdh, "TIME/TOTAL", (tr, ConsoleColor.Magenta),
                                                   (1 - tr, ConsoleColor.Black));
                PrintGraph(8, i_wdh, "TIME DISTR", (tdt_ct, ConsoleColor.DarkBlue),
                                                   (tdt_in, ConsoleColor.Blue),
                                                   (tdt_tt, ConsoleColor.Cyan));
                PrintGraph(8, i_wdh, "PASS/SKIP/FAIL", (res.Passed, ConsoleColor.Green),
                                                       (res.Failed, ConsoleColor.Yellow),
                                                       (res.Skipped, ConsoleColor.Red));
            }

            WriteLine();

            if (partial_results.Count > 0)
            {
                static void PrintColorDescription(ConsoleColor col, string description)
                {
                    Print("       ███ ", col);
                    PrintLine(description, ConsoleColor.White);
                }

                WriteLine("    GRAPH COLORS:");
                PrintColorDescription(ConsoleColor.Green, "Passed test methods");
                PrintColorDescription(ConsoleColor.Yellow, "Skipped test methods");
                PrintColorDescription(ConsoleColor.Red, "Failed test methods");
                PrintColorDescription(ConsoleColor.Magenta, "Time used for testing (relative to the total time)");
                PrintColorDescription(ConsoleColor.DarkBlue, "Time used for the module's static and instance constructors/destructors (.cctor, .ctor and .dtor)");
                PrintColorDescription(ConsoleColor.Blue, "Time used for the test initialization and cleanup method (@before and @after)");
                PrintColorDescription(ConsoleColor.Cyan, "Time used for the test method (@test)");
            }

            WriteLine();
            //PrintHeader("DETAILED TEST METHOD RESULTS", wdh);
            //WriteLine();
            WriteLine(new string('═', WIDTH));

            exitcode = failed; // NO FAILED TEST --> EXITCODE = 0

            #endregion
        }
        catch (Exception ex)
        {
            exitcode = -1;
            ForegroundColor = ConsoleColor.Red;

            WriteLine($"\n\n--- A CRITICAL ERROR OCCURRED ---:\n{ex}");
        }

        ForegroundColor = foreground;
        OutputEncoding = encoding;

        return exitcode;
    }
}

internal record struct MethodRunResults(Type Type, int Passed, int Failed, int Skipped, long TimeCtor, long TimeInit, long TimeMethod);

/// <summary>
/// A static class containing asserition utility methods, which extend the framework-provided class <see cref="Assert"/>.
/// </summary>
public static class AssertExtensions
{
    public static void AreSequentialEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) => Assert.IsTrue(expected.SequenceEqual(actual));

    public static void AreSetEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        T[] a1 = expected.ToArray();
        T[] a2 = actual.ToArray();

        Assert.AreEqual(a1.Length, a2.Length);

        AreSequentialEqual(a1.Except(a2), Array.Empty<T>());
        AreSequentialEqual(a2.Except(a1), Array.Empty<T>());
    }
}

/// <summary>
/// An exception which represents a skipped test method.
/// </summary>
public sealed class SkippedException
    : Exception
{
    internal SkippedException()
    {
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class TestWithAttribute
    : Attribute
{
    public object[] Arguments { get; }


    public TestWithAttribute(params object[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params bool[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params byte[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params sbyte[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params char[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params short[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params ushort[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params int[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params uint[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params long[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params ulong[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params float[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params double[] args) => Arguments = args.Select(t => (object)t).ToArray();

    public TestWithAttribute(params Type[] args) => Arguments = args;

    public TestWithAttribute(params Enum[] args) => Arguments = args;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class SkipAttribute
    : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class TestingPriorityAttribute
        : Attribute
{
    public uint Priority { get; }


    public TestingPriorityAttribute(uint p = 0) => Priority = p;
}
