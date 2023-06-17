using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace σκοπός {
  public static class Kerbalism {
    public static void ConsumeResource(Vessel v,
                                       string resource_name,
                                       double quantity,
                                       string title) {
      API.consume_resource.Invoke(
          null,
          new object[] {v, resource_name, quantity, title});
    }


    private static class API {
      public static System.Reflection.MethodInfo consume_resource {
         get {
           if (consume_resource_ == null) {
            foreach (var a in AssemblyLoader.loadedAssemblies) {
              if (a.name.StartsWith("Kerbalism") &&
                  !a.name.StartsWith("KerbalismBoot") &&
                  a.assembly.GetType("KERBALISM.API") is Type api
                  ) {
                consume_resource_ =api.GetMethod(
                    "ConsumeResource",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
              }
            }
          }
          return consume_resource_;
        }
      }
      private static System.Reflection.MethodInfo consume_resource_;
    }
  }
}
