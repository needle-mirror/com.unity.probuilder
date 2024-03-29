using UnityEngine;
using UnityEditor;
using UnityEditor.ProBuilder.UI;
using System.Linq;
using UnityEngine.ProBuilder.Stl;
using UnityEngine.ProBuilder;
using UnityEditor.ProBuilder;

namespace UnityEditor.ProBuilder.Actions
{
    sealed class ExportStlBinary : MenuAction
    {
        public override ToolbarGroup group { get { return ToolbarGroup.Export; } }
        public override string iconPath => string.Empty;
        public override Texture2D icon => null;
        public override TooltipContent tooltip { get { return _tooltip; } }

        static readonly TooltipContent _tooltip = new TooltipContent
            (
                "Export Stl",
                @"Export an Stl model file."
            );

        public override bool hidden
        {
            get { return true; }
        }

        public override bool enabled
        {
            get { return MeshSelection.selectedObjectCount > 0; }
        }

        protected override ActionResult PerformActionImplementation()
        {
            var res = ExportStlAscii.ExportWithFileDialog(Selection.gameObjects, FileType.Binary);

            if (string.IsNullOrEmpty(res))
                return new ActionResult(ActionResult.Status.Canceled, "User Canceled");

            Export.PingExportedModel(res);

            return new ActionResult(ActionResult.Status.Success, "Export STL");
        }
    }
}
