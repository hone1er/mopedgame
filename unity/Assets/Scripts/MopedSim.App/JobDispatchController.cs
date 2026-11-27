using System.Globalization;
using MopedSim.Core;
using MopedSim.UI;
using MopedSim.World;
using UnityEngine;

namespace MopedSim.App
{
    /// <summary>
    /// Owns the gig dispatch board (J / D-pad Left): opens the runtime-built <see cref="JobDispatchModalView"/>, forwards Accept /
    /// Abandon / Collect to <see cref="AppCompositionRoot"/> (which asks Core), and shows the accepted / delivered / failed toasts.
    /// Presentation and flow only: eligibility, payouts, reputation and the clock all live in MopedSim.Core. A delivered gig is
    /// collected automatically (Core still pays exactly once). Created lazily by <see cref="AppCompositionRoot.EnsureJobDispatch"/>;
    /// event-driven, so there is no per-frame work here.
    /// </summary>
    public sealed class JobDispatchController : MonoBehaviour
    {
        private AppCompositionRoot _app;
        private CourierGigController _courier;
        private JobDispatchModalView _view;
        private string _feedback = string.Empty;

        public JobDispatchModalView View { get { return _view; } }
        public bool IsOpen { get { return _view != null && _view.IsOpen; } }

        public void Initialize(AppCompositionRoot app)
        {
            _app = app;
            _courier = app != null ? app.Courier : null;
            if (_courier != null)
            {
                _courier.StatusChanged += OnStatusChanged;
            }
        }

        private void OnDestroy()
        {
            if (_courier != null)
            {
                _courier.StatusChanged -= OnStatusChanged;
            }

            if (_view != null)
            {
                _view.AcceptJobRequested -= OnAcceptRequested;
                _view.AbandonJobRequested -= OnAbandonRequested;
                _view.CollectRewardRequested -= OnCollectRequested;
                _view.CloseRequested -= Close;
            }
        }

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            _view = gameObject.AddComponent<JobDispatchModalView>();
            _view.Build(transform);
            _view.AcceptJobRequested += OnAcceptRequested;
            _view.AbandonJobRequested += OnAbandonRequested;
            _view.CollectRewardRequested += OnCollectRequested;
            _view.CloseRequested += Close;
        }

        /// <summary>Opens the board (Garage / Ride only). Returns whether it is open afterwards.</summary>
        public bool Open()
        {
            if (_app == null || !_app.IsBootstrapped || _courier == null)
            {
                return false;
            }

            if (IsOpen)
            {
                return true;
            }

            GameSessionState state = _app.Session != null ? _app.Session.CurrentState : GameSessionState.Garage;
            if (state != GameSessionState.Garage && state != GameSessionState.Ride)
            {
                return false;
            }

            EnsureView();
            _view.HideToast();
            _feedback = string.Empty;
            Refresh();
            _view.Show();
            _app.SetMenuInputBlocked(true);
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            _view.Hide();
            _app.SetMenuInputBlocked(_app.IsMenuOpen);
        }

        public bool Toggle()
        {
            if (IsOpen)
            {
                Close();
                return false;
            }

            return Open();
        }

        /// <summary>Re-renders the list from the current Core state.</summary>
        public void Refresh()
        {
            if (_view == null || _app == null)
            {
                return;
            }

            JobDefinition active = _courier != null ? _courier.ActiveJob : null;
            JobSessionStatus status = _courier != null ? _courier.Status : JobSessionStatus.Idle;
            bool canCollect = _courier != null && _courier.Jobs != null && _courier.Jobs.HasUncollectedReward;
            _view.RenderCatalog(_app.Catalog, _app.World, _app.ActiveBike, active, status, canCollect, _feedback);
        }

        private void OnAcceptRequested(string jobId)
        {
            JobAcceptResult result = _app.AcceptJob(jobId);
            if (!result.Accepted)
            {
                _feedback = result.Message;
                Refresh();
                return;
            }

            _feedback = string.Empty;
            Close();
            JobDefinition job = _courier != null ? _courier.ActiveJob : null;
            string title = job != null ? job.DisplayName : jobId;
            string first = job != null && job.Checkpoints.Count > 0 ? job.Checkpoints[0].DisplayName : string.Empty;
            _view.ShowToast("<b>GIG ACCEPTED</b>  " + title + (first.Length > 0 ? "\nHead to the pickup: " + first : string.Empty), HudStyle.Cool);
        }

        private void OnAbandonRequested()
        {
            if (_app.AbandonJob())
            {
                _feedback = "Gig abandoned - no pay.";
            }

            Refresh();
        }

        private void OnCollectRequested()
        {
            if (_app.CollectJobReward())
            {
                _feedback = "Reward collected.";
            }

            Refresh();
        }

        private void OnStatusChanged(JobSessionStatus status)
        {
            if (_courier == null || _app == null || !_app.IsBootstrapped)
            {
                return;
            }

            // The board may not have been opened yet when a gig finishes; the toast still needs the view.
            EnsureView();
            JobDefinition job = _courier.ActiveJob;
            string title = job != null ? job.DisplayName : string.Empty;
            switch (status)
            {
                case JobSessionStatus.Completed:
                    ShowDeliveredToast(title);
                    break;
                case JobSessionStatus.Failed:
                {
                    JobSessionSnapshot snap = _courier.GetSnapshot();
                    _view.ShowToast("<b>" + JobBoardHudView.SelectStatus(in snap) + "</b>  " + title + "\nNo pay. Reputation unchanged.", HudStyle.Bad);
                    break;
                }

                case JobSessionStatus.Abandoned:
                    _view.ShowToast("<b>GIG ABANDONED</b>  " + title + "\nNo pay. Reputation unchanged.", HudStyle.Warn);
                    break;
            }

            if (IsOpen)
            {
                Refresh();
            }
        }

        private void ShowDeliveredToast(string title)
        {
            CompletedGigRecord record = _courier.Jobs != null ? _courier.Jobs.LastCompletedRecord : null;
            decimal pay = record != null ? record.TotalPayoutUsd : 0m;
            int rep = record != null ? record.ReputationEarned : 0;
            bool collected = _app.CollectJobReward();
            string repNow = _app.World != null ? "  (rep " + _app.World.Reputation.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
            string text = "<b>DELIVERED</b>  " + title + "\n+$" + pay.ToString("0", CultureInfo.InvariantCulture)
                + "   |   +" + rep.ToString(CultureInfo.InvariantCulture) + " reputation" + repNow
                + (collected ? string.Empty : "   (collect at the dispatch board)");
            _view.ShowToast(text, HudStyle.Good);
        }
    }
}
