using System;
using System.Collections.Generic;
using LivingCompanionsValley.Models;

namespace LivingCompanionsValley.Services
{
    public static class WorkerGenerator
    {
        private static readonly string[] MaleNames = { "Pedro", "Juan", "Mateo", "Lucas", "Harvey", "Elliott", "Shane", "Sam", "Sebastian", "Clint", "Pierre", "Lewis", "Gus", "Willy" };
        private static readonly string[] FemaleNames = { "Laura", "Carmen", "Sofia", "Maria", "Penny", "Maru", "Leah", "Haley", "Emily", "Marnie", "Caroline", "Robin", "Jodi", "Abigail" };

        public static WorkerState GenerateApplicant()
        {
            var rand = new Random();
            var state = new WorkerState
            {
                Id = Guid.NewGuid().ToString()
            };

            // 1. Determinar Arquetipo de Género y Nombre
            state.Gender = (GenderArchetype)rand.Next(0, 2);
            if (state.Gender == GenderArchetype.Male)
            {
                state.Name = MaleNames[rand.Next(MaleNames.Length)];
            }
            else
            {
                state.Name = FemaleNames[rand.Next(FemaleNames.Length)];
            }

            // 2. Determinar Habilidades (1 al 10 en total, distribuidas aleatoriamente)
            state.FarmingLevel = rand.Next(1, 7);
            state.ForagingLevel = rand.Next(1, 7);
            state.MiningLevel = rand.Next(1, 7);
            state.FishingLevel = rand.Next(1, 7);
            state.CombatLevel = rand.Next(1, 7);

            // 3. Determinar Apellido o Título basado en su habilidad más alta o su origen
            state.Surname = GenerateSurname(state, rand);

            // 4. Determinar Rasgo (Trait)
            var traits = (WorkerTrait[])Enum.GetValues(typeof(WorkerTrait));
            state.Trait = traits[rand.Next(traits.Length)];

            // 5. Calcular Salario Basado en Habilidades y Rasgo
            state.Wage = CalculateWage(state);

            // 6. Determinar Fenotipo Estético (Aesthetic Profile)
            // Lighter: 35%, Medium: 40%, Dark: 20%, PelicanTown: 5%
            int roll = rand.Next(0, 100);
            if (roll < 35)
            {
                state.Aesthetic = AestheticProfile.Lighter;
            }
            else if (roll < 75)
            {
                state.Aesthetic = AestheticProfile.Medium;
            }
            else if (roll < 95)
            {
                state.Aesthetic = AestheticProfile.Dark;
            }
            else
            {
                state.Aesthetic = AestheticProfile.PelicanTown;
            }

            ApplyAestheticProfile(state, rand);

            return state;
        }

        private static string GenerateSurname(WorkerState state, Random rand)
        {
            int maxSkill = Math.Max(state.FarmingLevel, 
                           Math.Max(state.ForagingLevel, 
                           Math.Max(state.MiningLevel, 
                           Math.Max(state.FishingLevel, state.CombatLevel))));

            var candidates = new List<string>();

            if (maxSkill == state.FarmingLevel)
            {
                candidates.AddRange(new[] { "Miller", "Gardener", "Cosechador", "Granjero", "Wheat" });
            }
            if (maxSkill == state.ForagingLevel)
            {
                candidates.AddRange(new[] { "Forester", "Leñador", "Ranger", "Silvestre", "Wood" });
            }
            if (maxSkill == state.MiningLevel)
            {
                candidates.AddRange(new[] { "Stone", "Minero", "Excavador", "Hierro", "Clay" });
            }
            if (maxSkill == state.FishingLevel)
            {
                candidates.AddRange(new[] { "Fisher", "Pescador", "Marino", "Caster", "Hook" });
            }
            if (maxSkill == state.CombatLevel)
            {
                candidates.AddRange(new[] { "Hunter", "Cazador", "Slayer", "Guerrero", "Steel" });
            }

            // Orígenes y títulos genéricos siempre viables
            candidates.AddRange(new[] { "de Zuzu", "del Valle", "de Grample", "Slicker" });

            return candidates[rand.Next(candidates.Count)];
        }

        public static int CalculateWage(WorkerState state)
        {
            double baseWage = 20.0;
            baseWage += state.FarmingLevel * 8.0;
            baseWage += state.ForagingLevel * 8.0;
            baseWage += state.MiningLevel * 8.0;
            baseWage += state.FishingLevel * 8.0;
            baseWage += state.CombatLevel * 8.0;

            double multiplier = 1.0;
            switch (state.Trait)
            {
                case WorkerTrait.Workaholic:
                    multiplier = 1.20; // 20% más caro, pero trabaja más rápido
                    break;
                case WorkerTrait.GreenThumb:
                    multiplier = 1.10;
                    break;
                case WorkerTrait.Clumsy:
                    multiplier = 0.85; // 15% de descuento por torpeza
                    break;
                case WorkerTrait.NightOwl:
                    multiplier = 1.10;
                    break;
                case WorkerTrait.EarlyBird:
                    multiplier = 1.00;
                    break;
                case WorkerTrait.CitySlicker:
                    multiplier = 1.15;
                    break;
            }

            return (int)Math.Round(baseWage * multiplier);
        }

        private static void ApplyAestheticProfile(WorkerState state, Random rand)
        {
            // Estilos de peinados y camisas estándar de Stardew Valley
            state.HairStyle = rand.Next(0, 36);
            state.Shirt = rand.Next(0, 100);
            state.Pants = rand.Next(0, 4);

            switch (state.Aesthetic)
            {
                case AestheticProfile.Lighter:
                    state.SkinColor = rand.Next(0, 4); // Tonos claros
                    
                    // Colores de pelo claros (Rubio, Pelirrojo, Castaño Claro)
                    int hairRoll = rand.Next(0, 10);
                    if (hairRoll < 6) // Rubio (60%)
                    {
                        state.HairColorR = rand.Next(220, 256);
                        state.HairColorG = rand.Next(180, 230);
                        state.HairColorB = rand.Next(40, 100);
                    }
                    else if (hairRoll < 8) // Pelirrojo (20%)
                    {
                        state.HairColorR = rand.Next(180, 230);
                        state.HairColorG = rand.Next(50, 110);
                        state.HairColorB = rand.Next(20, 60);
                    }
                    else // Castaño Claro (20%)
                    {
                        state.HairColorR = rand.Next(100, 150);
                        state.HairColorG = rand.Next(60, 100);
                        state.HairColorB = rand.Next(30, 70);
                    }
                    break;

                case AestheticProfile.Medium:
                    state.SkinColor = rand.Next(4, 9); // Tonos trigueños/bronceados

                    // Colores de pelo intermedios (Castaño Oscuro, Negro, Cobrizo)
                    int hairRollMed = rand.Next(0, 10);
                    if (hairRollMed < 7) // Castaño Oscuro/Negro (70%)
                    {
                        state.HairColorR = rand.Next(20, 60);
                        state.HairColorG = rand.Next(15, 45);
                        state.HairColorB = rand.Next(10, 30);
                    }
                    else if (hairRollMed < 9) // Cobrizo (20%)
                    {
                        state.HairColorR = rand.Next(120, 170);
                        state.HairColorG = rand.Next(40, 80);
                        state.HairColorB = rand.Next(20, 50);
                    }
                    else // Castaño Medio (10%)
                    {
                        state.HairColorR = rand.Next(70, 100);
                        state.HairColorG = rand.Next(45, 75);
                        state.HairColorB = rand.Next(25, 55);
                    }
                    break;

                case AestheticProfile.Dark:
                    state.SkinColor = rand.Next(9, 16); // Tonos oscuros

                    // Colores de pelo oscuros (Negro, Canoso, Rubio Platinado raro)
                    int hairRollDark = rand.Next(0, 100);
                    if (hairRollDark < 90) // Negro/Marrón Oscurísimo (90%)
                    {
                        state.HairColorR = rand.Next(10, 30);
                        state.HairColorG = rand.Next(10, 30);
                        state.HairColorB = rand.Next(10, 30);
                    }
                    else if (hairRollDark < 95) // Canoso (5%)
                    {
                        state.HairColorR = rand.Next(180, 210);
                        state.HairColorG = rand.Next(180, 210);
                        state.HairColorB = rand.Next(180, 210);
                    }
                    else // Rubio Platinado (5%)
                    {
                        state.HairColorR = rand.Next(200, 240);
                        state.HairColorG = rand.Next(180, 220);
                        state.HairColorB = rand.Next(100, 140);
                    }
                    break;

                case AestheticProfile.PelicanTown:
                    state.SkinColor = rand.Next(0, 12);

                    // Pelo de fantasía (Morado, Azul, Rosa, Verde)
                    int hairRollPeli = rand.Next(0, 4);
                    if (hairRollPeli == 0) // Morado (Abigail)
                    {
                        state.HairColorR = rand.Next(100, 160);
                        state.HairColorG = rand.Next(30, 80);
                        state.HairColorB = rand.Next(150, 220);
                    }
                    else if (hairRollPeli == 1) // Azul (Emily)
                    {
                        state.HairColorR = rand.Next(30, 80);
                        state.HairColorG = rand.Next(80, 140);
                        state.HairColorB = rand.Next(200, 255);
                    }
                    else if (hairRollPeli == 2) // Rosa
                    {
                        state.HairColorR = rand.Next(200, 255);
                        state.HairColorG = rand.Next(50, 120);
                        state.HairColorB = rand.Next(120, 180);
                    }
                    else // Verde
                    {
                        state.HairColorR = rand.Next(40, 100);
                        state.HairColorG = rand.Next(150, 220);
                        state.HairColorB = rand.Next(50, 120);
                    }
                    break;
            }

            // Colores bonitos para pantalones (Denim, Gris, Khaki, Verde militar, Guinda)
            int[][] pantsPalettes = new int[][]
            {
                new int[] { 30, 60, 150 }, // Azul Denim
                new int[] { 40, 40, 40 },  // Carbón
                new int[] { 140, 110, 80 }, // Khaki
                new int[] { 50, 80, 50 },   // Verde Oliva
                new int[] { 120, 40, 40 }   // Guinda
            };
            int[] chosenPants = pantsPalettes[rand.Next(pantsPalettes.Length)];
            state.PantsColorR = Math.Clamp(chosenPants[0] + rand.Next(-10, 10), 0, 255);
            state.PantsColorG = Math.Clamp(chosenPants[1] + rand.Next(-10, 10), 0, 255);
            state.PantsColorB = Math.Clamp(chosenPants[2] + rand.Next(-10, 10), 0, 255);
        }
    }
}
