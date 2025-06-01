using NCalc;
using System.Globalization;

namespace TeeTimeTally.API.Services;


public static class FinancialValidationService
{
	public static async Task<(bool IsValid, List<string> Errors)> ValidateConfigurationAsync(
		decimal buyInAmount,
		string skinValueFormula,
		string cthPayoutFormula,
		ILogger logger)
	{
		var errors = new List<string>();
		bool overallIsValid = true;

		if (buyInAmount <= 0)
		{
			errors.Add("Buy-in amount must be greater than zero.");
			overallIsValid = false;
		}
		if (string.IsNullOrWhiteSpace(skinValueFormula))
		{
			errors.Add("Skin value formula cannot be empty.");
			overallIsValid = false;
		}
		if (string.IsNullOrWhiteSpace(cthPayoutFormula))
		{
			errors.Add("Closest to the Hole (CTH) payout formula cannot be empty.");
			overallIsValid = false;
		}

		if (!overallIsValid)
		{
			return (false, errors);
		}

		const int minPlayers = 6;
		const int maxPlayers = 30;
		const int numberOfHoles = 18;

		for (int playerCount = minPlayers; playerCount <= maxPlayers; playerCount++)
		{
			var formulaParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
			{
				{ "roundPlayers", playerCount }
			};

			try
			{
				decimal totalPot = playerCount * buyInAmount;

				// Evaluate formulas asynchronously
				(bool isSkinFormulaEvaluated, decimal skinValue) = await EvaluateFormulaAsync(skinValueFormula, formulaParameters, logger);
				if (!isSkinFormulaEvaluated)
				{
					errors.Add($"Skin value formula ('{skinValueFormula}') is invalid. Please check syntax and ensure it results in a number (for {playerCount} players).");
					overallIsValid = false;
				}
				else if (skinValue < 0)
				{
					errors.Add($"Calculated skin value is negative ({skinValue:C}) for {playerCount} players using formula '{skinValueFormula}'.");
					overallIsValid = false;
				}

				(bool isCthFormulaEvaluated, decimal cthPayout) = await EvaluateFormulaAsync(cthPayoutFormula, formulaParameters, logger);
				if (!isCthFormulaEvaluated)
				{
					errors.Add($"CTH payout formula ('{cthPayoutFormula}') is invalid. Please check syntax and ensure it results in a number (for {playerCount} players).");
					overallIsValid = false;
				}
				else if (cthPayout < 0)
				{
					errors.Add($"Calculated CTH payout is negative ({cthPayout:C}) for {playerCount} players using formula '{cthPayoutFormula}'.");
					overallIsValid = false;
				}

				if (!isSkinFormulaEvaluated || !isCthFormulaEvaluated || skinValue < 0 || cthPayout < 0)
				{
					overallIsValid = false;
					continue;
				}

				decimal totalPotentialSkinsValue = numberOfHoles * skinValue;
				decimal remainingForWinner = totalPot - totalPotentialSkinsValue - cthPayout;

				if (remainingForWinner <= 0)
				{
					errors.Add($"Configuration is invalid for {playerCount} players: Does not guarantee a positive payout for the overall winner (Remaining: {remainingForWinner:C}). Pot: {totalPot:C}, Skins Total: {totalPotentialSkinsValue:C}, CTH: {cthPayout:C}.");
					overallIsValid = false;
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error during financial simulation for {PlayerCount} players. BuyIn: {BuyIn}, SkinFormula: {SkinFormula}, CthFormula: {CthFormula}",
					playerCount, buyInAmount, skinValueFormula, cthPayoutFormula);
				errors.Add($"An unexpected error occurred during simulation for {playerCount} players.");
				overallIsValid = false;
			}
		}

		if (!overallIsValid && !errors.Any(e => e.StartsWith("Buy-in amount") || e.StartsWith("Skin value formula cannot be empty") || e.StartsWith("Closest to the Hole")))
		{
			if (errors.All(e => !e.Contains("Does not guarantee a positive payout")))
			{
				errors.Insert(0, "The financial configuration is invalid due to issues found during player count simulations (6-30).");
			}
		}
		return (overallIsValid, errors);
	}

	/// <summary>
	/// Evaluates a given formula string using NCalcAsync with provided parameters.
	/// Assumes formulas use parameter names directly (e.g., "roundPlayers").
	/// </summary>
	public static async Task<(bool Success, decimal Value)> EvaluateFormulaAsync(string formula, IDictionary<string, object> parameters, ILogger logger)
	{
		if (string.IsNullOrWhiteSpace(formula))
		{
			return (false, 0);
		}
		try
		{
			// NCalcAsync might use ExpressionContext for more advanced scenarios or options
			var expression = new AsyncExpression(formula, ExpressionOptions.IgnoreCaseAtBuiltInFunctions);

			foreach (var param in parameters)
			{
				expression.Parameters[param.Key] = param.Value;
			}

			object? evaluationResult = await expression.EvaluateAsync(); // Use EvaluateAsync

			if (expression.HasErrors()) // Check for errors after evaluation
			{
				logger.LogWarning("NCalcAsync evaluation error for formula '{Formula}': {Error}", formula, expression.Error);
				return (false, 0);
			}

			if (evaluationResult != null)
			{
				// NCalc can return various numeric types (int, double, long, decimal).
				// Convert safely to decimal.
				return (true, Convert.ToDecimal(evaluationResult, CultureInfo.InvariantCulture));
			}

			logger.LogWarning("NCalcAsync evaluation for formula '{Formula}' resulted in null.", formula);
			return (false, 0);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Exception evaluating formula '{Formula}' with NCalcAsync. Parameters: {@Parameters}", formula, parameters);
			return (false, 0);
		}
	}
}