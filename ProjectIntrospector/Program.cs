namespace ProjectIntrospector
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("❌ 请提供项目根目录路径。");
                return;
            }

            string rootPath = args[0];
            string? entityPath = null;
            string filterPattern = "bin|obj|.git|.vs|node_modules|TestResults|packages";

            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--entities")
                {
                    entityPath = args[i + 1];
                }
                if (args[i] == "--filter")
                {
                    filterPattern = args[i + 1];
                }
            }

            if (!Directory.Exists(rootPath))
            {
                Console.WriteLine($"❌ 目录不存在: {rootPath}");
                return;
            }

            var inspector = new CodeInspector(rootPath, entityPath, filterPattern);
            inspector.Run();

            Console.WriteLine("✅ 已生成：project_structure.md 到项目目录。");
        }
    }
}