using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Threading.Tasks;
using Nolvus.Core.Services;
using Nolvus.StockGame.Core;

namespace Nolvus.StockGame.Meta
{
    public class GameFile
    {
        public string Name { get; set; }
        public FileLocation Location { get; set; }
        public string Hash { get; set; }
        public bool FileSkip { get; set; }
        public bool HashSkip { get; set; }       

        public void Parse(XmlNode Node)
        {
            Name = Node["Name"].InnerText;
            Location = (FileLocation)Enum.Parse(typeof(FileLocation), Node["Location"].InnerText);
            Hash = Node["Hash"].InnerText;

            HashSkip = false;
            FileSkip = false;

            if (Node["HashSkip"] != null)
            {
                HashSkip = Convert.ToBoolean(Node["HashSkip"].InnerText);
            }
            if (Node["FileSkip"] != null)
            {
                FileSkip = Convert.ToBoolean(Node["FileSkip"].InnerText);
            }
        }

        private string GetGameDir(string Dir)
        {            
            switch (this.Location)
            {
                case FileLocation.Root:
                    return Dir;                    
                case FileLocation.Data:
                    return Path.Combine(Dir, "Data");
                default:
                    throw new ArgumentOutOfRangeException();                    
            }            
        }       

        private static string FindCaseInsensitive(string directory, string fileName)
        {
            if (!Directory.Exists(directory))
                return null;
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveFilePath(string exactPath)
        {
            if (File.Exists(exactPath))
                return exactPath;
            var dir = Path.GetDirectoryName(exactPath);
            var name = Path.GetFileName(exactPath);
            return FindCaseInsensitive(dir, name);
        }

        public void Check(string GameDir, string LgCode)
        {
            string FileName = Path.Combine(this.GetGameDir(GameDir), this.Name);
            string resolved = ResolveFilePath(FileName);

            if (resolved == null)
            {
                if (!this.FileSkip || (this.Name == string.Format("Skyrim - Voices_{0}0.bsa", LgCode.ToLower())))
                {
                    throw new GameFileMissingException("Game file : " + FileName + " does not exist!");
                }
            }
            else if ((ServiceSingleton.Files.GetHash(resolved) != this.Hash) && !this.HashSkip)
            {
                throw new GameFileIntegrityException("Hash for game file : " + resolved + " does not match!");
            }
        }

        public string GetFullName(string Dir)
        {
            return Path.Combine(this.GetGameDir(Dir), this.Name);
        }

        public void Copy(string GameDir, string StockGameDir, string LgCode)
        {
            string SourceFileName = this.GetFullName(GameDir);
            string DestinationFileName = this.GetFullName(StockGameDir);

            FileInfo DestFileInfo = new FileInfo(DestinationFileName);

            if (!Directory.Exists(DestFileInfo.Directory.FullName))
            {
                Directory.CreateDirectory(DestFileInfo.Directory.FullName);
            }

            string resolved = ResolveFilePath(SourceFileName);
            if (resolved != null)
            {
                File.Copy(resolved, DestinationFileName, true);
            }
            else if (!this.FileSkip || (this.Name == string.Format("Skyrim - Voices_{0}0.bsa", LgCode.ToLower())))
            {
                throw new GameFileMissingException("Game file : " + SourceFileName + " does not exist!");
            }
        }

    }
}
