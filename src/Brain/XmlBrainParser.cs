using System;
using System.Xml.Linq;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using System.Text.RegularExpressions;

namespace StardewLivingValley.Brain
{
    public class XmlBrainParser
    {
        private readonly IMonitor _logger;

        public XmlBrainParser(IMonitor logger)
        {
            _logger = logger;
        }

        public string ProcessBrainXml(string rawXml, string npcName, int currentHearts)
        {
            if (string.IsNullOrWhiteSpace(rawXml) || !rawXml.Contains("<NPCBrain>"))
                return rawXml;

            try
            {
                var doc = XDocument.Parse(rawXml);
                
                // Find all <CurrentState> nodes
                var currentStateNodes = doc.Descendants("CurrentState").ToList();
                
                foreach (var node in currentStateNodes)
                {
                    var checkAttribute = node.Attribute("check");
                    if (checkAttribute != null)
                    {
                        bool isConditionMet = EvaluateCondition(checkAttribute.Value, npcName, currentHearts);
                        
                        if (!isConditionMet)
                        {
                            // Remove the node entirely if condition fails
                            node.Remove();
                        }
                        else
                        {
                            // If condition is met, unwrap the children (replace the <CurrentState> wrapper with its contents)
                            // This makes the prompt cleaner for the LLM
                            node.ReplaceWith(node.Nodes());
                        }
                    }
                }

                // Return the cleaned up XML
                return doc.ToString();
            }
            catch (Exception ex)
            {
                _logger.Log($"[XmlBrainParser] Error parsing XML for {npcName}: {ex.Message}", LogLevel.Error);
                return rawXml;
            }
        }

        private bool EvaluateCondition(string condition, string npcName, int currentHearts)
        {
            condition = condition.Trim();

            // Check Married State
            if (condition.Equals("MarriedToPlayer", StringComparison.OrdinalIgnoreCase))
            {
                return Game1.player.spouse == npcName;
            }
            if (condition.Equals("NotMarried", StringComparison.OrdinalIgnoreCase))
            {
                return Game1.player.spouse != npcName;
            }

            // Check Hearts (e.g. "Hearts < 6", "Hearts >= 8")
            var match = Regex.Match(condition, @"Hearts\s*(<|<=|>|>=|==)\s*(\d+)");
            if (match.Success)
            {
                string op = match.Groups[1].Value;
                int targetHearts = int.Parse(match.Groups[2].Value);

                return op switch
                {
                    "<" => currentHearts < targetHearts,
                    "<=" => currentHearts <= targetHearts,
                    ">" => currentHearts > targetHearts,
                    ">=" => currentHearts >= targetHearts,
                    "==" => currentHearts == targetHearts,
                    _ => false
                };
            }

            _logger.Log($"[XmlBrainParser] Unknown condition: {condition}", LogLevel.Warn);
            return false;
        }
    }
}
