using Microsoft.Framework.Runtime.Roslyn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dbus.Sharp
{
    public class ClassGenerator : ICompileModule
    {
        public void BeforeCompile(BeforeCompileContext context)
        {
            Console.WriteLine("I am running");
        }

        public void AfterCompile(AfterCompileContext context)
        {
        }
    }
}
