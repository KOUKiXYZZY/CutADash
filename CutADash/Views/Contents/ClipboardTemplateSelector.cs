using CutADash.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CutADash.Views.Contents
{
    public class ClipboardTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? TextTemplate { get; set; }
        public DataTemplate? ImageTemplate { get; set; }
        public DataTemplate? FilesTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            var clip = (ClipboardItem)item;

            return clip.Type switch
            {
                ClipboardContentType.Image => ImageTemplate,
                ClipboardContentType.Files => FilesTemplate,
                                         _ => TextTemplate
            };
        }
    }
}
