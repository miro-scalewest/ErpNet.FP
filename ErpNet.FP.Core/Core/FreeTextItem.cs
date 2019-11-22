namespace ErpNet.FP.Core
{
    using System.Runtime.Serialization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public enum LineHeight
    {
        OneLine = 1,
        TwoLines = 2,
    }
    
    /// <summary>
    /// Represents one free text line in a system receipt. 
    /// </summary>
    public class FreeTextItem
    {
        /// <summary>
        /// Gets or sets the text of the line.
        /// </summary>
        /// <value>
        /// The text.
        /// </value>
        [JsonProperty(Required = Required.Always)]
        public string Text { get; set; } = "";

        /// <summary>
        /// Gets or sets the bold style status of the line.
        /// </summary>
        /// <value>
        /// The bold status.
        /// </value>
        public bool Bold { get; set; } = false;

        /// <summary>
        /// Gets or sets the italic style status of the line.
        /// </summary>
        /// <value>
        /// The italic status.
        /// </value>
        public bool Italic { get; set; } = false;

        /// <summary>
        /// Gets or sets the underline style status of the line.
        /// </summary>
        /// <value>
        /// The underline status.
        /// </value>
        public bool Underline { get; set; } = false;

        /// <summary>
        /// Gets or sets the italic style status of the line.
        /// </summary>
        /// <value>
        /// The italic status.
        /// </value>
        public bool Size { get; set; } = false;

        /// <summary>
        /// Gets or sets the line height. 
        /// </summary>
        /// <value>
        /// The line height.
        /// </value>        
        public LineHeight LineHeight { get; set; } = LineHeight.OneLine;
    }
}