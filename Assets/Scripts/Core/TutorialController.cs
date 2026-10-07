using System;
using System.Collections;
using StackSurge.Core;
using StackSurge.UI;
using StackSurge.Meta;
using UnityEngine;

namespace StackSurge
{
    /// <summary>
    /// Handles the state machine and logic for the simulated sandbox interactive tutorial.
    /// </summary>
    public class TutorialController
    {
        private readonly StackSurgeGame _game;
        private GameView _view => _game.View;

        public TutorialController(StackSurgeGame game)
        {
            _game = game;
        }

        public IEnumerator PlayTutorialCoroutine()
        {
            _game.InTutorial = true;
            _game.TutorialRequiredColumn = -1;
            
            // Initialize board to clean slate
            _game.InitializeBoardForTutorial();
            _view.RefreshGrid(_game.Board.Cells);
            _game.RunUpdateHud();

            // STEP 1: Welcome Dialog
            yield return _view.ShowTutorialDialogCoroutine(
                "Welcome to Stack Surge!",
                "Stack Surge is a fast-paced cascade matching game. The goal is to clear tiles and survive the rising pressure!",
                showNextButton: true
            );

            // STEP 2: Basic Match-3 (Vertical)
            _game.Board.SetCell(2, 0, TileKind.Red);
            _game.Board.SetCell(2, 1, TileKind.Red);
            _game.Current = TileKind.Red;
            _game.Next = TileKind.Blue;
            _view.RefreshGrid(_game.Board.Cells);
            _game.RunUpdateHud();

            yield return _view.ShowTutorialDialogCoroutine(
                "Vertical Matching",
                "Match 3 or more adjacent tiles of the same color vertically or horizontally to clear them.\n\nTap Column 3 (the middle column) to drop your Red tile!",
                showNextButton: false
            );

            _view.HighlightColumn(2);
            _game.TutorialRequiredColumn = 2;

            yield return WaitForActionAndResolution();
            _game.TutorialRequiredColumn = -1; // no drops between steps / under the dialogs
            _view.ClearColumnHighlight();
            _view.HideTutorialDialog();

            yield return new WaitForSeconds(0.5f);

            // STEP 3: Horizontal Wild Match
            _game.Board.SetCell(1, 0, TileKind.Blue);
            _game.Board.SetCell(3, 0, TileKind.Blue);
            _game.Current = TileKind.Wild;
            _game.Next = TileKind.Bomb;
            _view.RefreshGrid(_game.Board.Cells);
            _game.RunUpdateHud();

            yield return _view.ShowTutorialDialogCoroutine(
                "Wild Tiles & Horizontal Matches",
                "White tiles are WILD! They match with any color.\n\nTap Column 3 to place the Wild tile between the Blue tiles to clear them horizontally!",
                showNextButton: false
            );

            _view.HighlightColumn(2);
            _game.TutorialRequiredColumn = 2;

            yield return WaitForActionAndResolution();
            _game.TutorialRequiredColumn = -1; // no drops between steps / under the dialogs
            _view.ClearColumnHighlight();
            _view.HideTutorialDialog();

            yield return new WaitForSeconds(0.5f);

            // STEP 4: Bombs
            _game.Board.SetCell(1, 0, TileKind.Red);
            _game.Board.SetCell(1, 1, TileKind.Green);
            _game.Board.SetCell(2, 0, TileKind.Purple);
            _game.Board.SetCell(2, 1, TileKind.Blue);
            _game.Board.SetCell(3, 0, TileKind.Yellow);
            _game.Board.SetCell(3, 1, TileKind.Red);
            _game.Current = TileKind.Bomb;
            _game.Next = TileKind.Green;
            _view.RefreshGrid(_game.Board.Cells);
            _game.RunUpdateHud();

            yield return _view.ShowTutorialDialogCoroutine(
                "Bombs",
                "Bombs explode a 3x3 grid around them when dropped, regardless of color matching.\n\nTap Column 3 to clear the obstacle block!",
                showNextButton: false
            );

            _view.HighlightColumn(2);
            _game.TutorialRequiredColumn = 2;

            yield return WaitForActionAndResolution();
            _game.TutorialRequiredColumn = -1; // no drops between steps / under the dialogs
            _view.ClearColumnHighlight();
            _view.HideTutorialDialog();

            yield return new WaitForSeconds(0.5f);

            // STEP 5: Rising Rows
            yield return _view.ShowTutorialDialogCoroutine(
                "Rising Pressure",
                "During a normal game, the tiles will automatically rise from the bottom. Let's see what happens.",
                showNextButton: true
            );

            _view.HideTutorialDialog();
            
            // Programmatically trigger a row rise
            yield return _game.RunRiseRowAndResolve();
            yield return new WaitUntil(() => !_game.ResolvingMatches);

            yield return new WaitForSeconds(0.5f);

            yield return _view.ShowTutorialDialogCoroutine(
                "Survival is Key",
                "If the tiles rise all the way to the top of the board, it's Game Over! Keep making matches and using bombs to stay alive.",
                showNextButton: true
            );

            // STEP 6: Outro
            yield return _view.ShowTutorialDialogCoroutine(
                "Tutorial Completed!",
                "You are now ready to play Stack Surge. Match tiles, score combos, and challenge the daily leaderboard!",
                showNextButton: true
            );

            _view.HideTutorialDialog();
            _game.InTutorial = false;
            _game.TutorialRequiredColumn = -1;

            // Save completed status
            _game.Save.TutorialCompleted = true;
            LocalProgress.Save(_game.Save);

            // Start the actual game!
            _game.RunBeginRun();
        }

        private IEnumerator WaitForActionAndResolution()
        {
            yield return new WaitUntil(() => _game.ResolvingMatches);
            yield return new WaitUntil(() => !_game.ResolvingMatches);
        }
    }
}
