using System.Runtime.CompilerServices;

// The presentation mappers are internal so a view cannot reach them; the tests that hold their copy and
// labelling to #222 / PRN-03 live in the test assembly.
[assembly: InternalsVisibleTo("WebMVC.Tests")]
