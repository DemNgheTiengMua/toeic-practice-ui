using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

public class AdminLessonException(string code, string message, int status = 400) : Exception(message)
{
    public string Code => code;
    public int Status => status;
}
