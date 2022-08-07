using System;
using System.Runtime.CompilerServices;

namespace principia {
namespace ksp_plugin_adapter {

public static class Log {
  public static void Fatal(string message,
                           [CallerFilePath] string file = "",
                           [CallerLineNumber] int line = -1) {
    UnityEngine.Debug.LogError($"{file}:{line} {message}");
    Console.Error.WriteLine($"{file}:{line} {message}");
    Environment.Exit(1);
  }
}

}
}
