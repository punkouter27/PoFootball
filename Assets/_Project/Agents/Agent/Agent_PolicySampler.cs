using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace PoFootball.Agents
{
    /// <summary>
    /// Estimates a trained brain's chances over one discrete branch by running its
    /// .onnx on a batch of copies of one observation and counting what comes back.
    ///
    /// WHY SAMPLING AND NOT THE LOGITS. Since ML-Agents 2.x the exported graph
    /// samples the discrete action itself (a Multinomial node behind the softmax)
    /// and outputs only the drawn index — "discrete_actions" — and its argmax;
    /// the probabilities never leave the graph, and ML-Agents' own ModelRunner
    /// never sees them either. The draw is made independently per batch row, so
    /// SAMPLES identical rows are SAMPLES draws from exactly the distribution the
    /// brain played from. At 64 samples a 50% call reads within about six points,
    /// which is finer than a viewer reads a bar.
    ///
    /// A SECOND WORKER, NOT ML-AGENTS' OWN. The agent's ModelRunner is internal to
    /// ML-Agents and is shared by every agent on the behavior; running a batch of
    /// 64 through it is not something it offers. This worker is built once per
    /// coordinator, on the CPU like the trainer's, and costs one batched inference
    /// per play — a few hundred microseconds — and only when someone is watching
    /// (Systems_ICallSheetSink.Available) and a model is loaded.
    ///
    /// NEVER A DEPENDENCY OF THE GAME. Any failure — an input it does not
    /// recognise, a shape mismatch, a backend error — turns sampling off for the
    /// rest of the session and the call sheet falls back to the choice alone.
    /// </summary>
    internal sealed class Agent_PolicySampler : IDisposable
    {
        internal const int SAMPLES = PoFootball.Systems.Systems_CallSheet.POLICY_SAMPLES;

        private const string OBSERVATION_INPUT = "obs_0";
        private const string MASK_INPUT = "action_masks";
        private const string DISCRETE_OUTPUT = "discrete_actions";

        private readonly int _observationSize;
        private readonly int _branchSize;
        private readonly float[] _observationBatch;
        private readonly float[] _maskBatch;

        private Worker _worker;
        private Tensor<float> _observationTensor;
        private Tensor<float> _maskTensor;
        private bool _hasMaskInput;
        private bool _failed;

        private Agent_PolicySampler(int observationSize, int branchSize)
        {
            _observationSize = observationSize;
            _branchSize = branchSize;
            _observationBatch = new float[SAMPLES * observationSize];
            _maskBatch = new float[SAMPLES * branchSize];
        }

        /// <summary>
        /// A sampler for a brain with one vector observation and a single discrete
        /// branch, or null when the model cannot be read that way.
        /// </summary>
        internal static Agent_PolicySampler Create(ModelAsset modelAsset, int observationSize, int branchSize)
        {
            if (modelAsset == null)
            {
                return null;
            }

            Agent_PolicySampler sampler = new Agent_PolicySampler(observationSize, branchSize);

            try
            {
                Model model = ModelLoader.Load(modelAsset);
                bool hasObservation = false;

                for (int index = 0; index < model.inputs.Count; index++)
                {
                    string name = model.inputs[index].name;
                    hasObservation |= name == OBSERVATION_INPUT;
                    sampler._hasMaskInput |= name == MASK_INPUT;
                }

                if (!hasObservation)
                {
                    Debug.LogWarning(
                        $"[PoFootball] {modelAsset.name} has no '{OBSERVATION_INPUT}' input; "
                        + "the call sheet will show the brain's choice without its chances.");
                    return null;
                }

                sampler._worker = new Worker(model, BackendType.CPU);
                sampler._observationTensor = new Tensor<float>(new TensorShape(SAMPLES, observationSize));

                if (sampler._hasMaskInput)
                {
                    sampler._maskTensor = new Tensor<float>(new TensorShape(SAMPLES, branchSize));
                }

                return sampler;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[PoFootball] Could not build a policy sampler for {modelAsset.name}: "
                    + exception.Message);
                sampler.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Fills <paramref name="chances"/> (branchSize long) with the share of
        /// SAMPLES draws that landed on each index. <paramref name="enabled"/> is
        /// the action mask, true for an allowed index.
        /// </summary>
        internal bool TrySample(float[] observation, bool[] enabled, float[] chances)
        {
            if (_failed || _worker == null)
            {
                return false;
            }

            try
            {
                for (int row = 0; row < SAMPLES; row++)
                {
                    Array.Copy(observation, 0, _observationBatch, row * _observationSize, _observationSize);

                    for (int slot = 0; slot < _branchSize; slot++)
                    {
                        _maskBatch[(row * _branchSize) + slot] = enabled[slot] ? 1f : 0f;
                    }
                }

                _observationTensor.Upload(_observationBatch);
                _worker.SetInput(OBSERVATION_INPUT, _observationTensor);

                if (_hasMaskInput)
                {
                    _maskTensor.Upload(_maskBatch);
                    _worker.SetInput(MASK_INPUT, _maskTensor);
                }

                _worker.Schedule();

                Tensor output = _worker.PeekOutput(DISCRETE_OUTPUT);
                Array.Clear(chances, 0, chances.Length);

                using (Tensor readback = output.ReadbackAndClone())
                {
                    if (readback is Tensor<int> indices)
                    {
                        for (int row = 0; row < SAMPLES; row++)
                        {
                            Count(indices[row, 0], chances);
                        }
                    }
                    else if (readback is Tensor<float> values)
                    {
                        for (int row = 0; row < SAMPLES; row++)
                        {
                            Count(Mathf.RoundToInt(values[row, 0]), chances);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"'{DISCRETE_OUTPUT}' has an unexpected type {readback.GetType().Name}");
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                _failed = true;
                Debug.LogWarning(
                    "[PoFootball] Policy sampling failed and is off for this session; "
                    + "the call sheet will show the brain's choice without its chances. "
                    + exception.Message);
                return false;
            }
        }

        private void Count(int index, float[] chances)
        {
            if (index >= 0 && index < chances.Length)
            {
                chances[index] += 1f / SAMPLES;
            }
        }

        public void Dispose()
        {
            _observationTensor?.Dispose();
            _maskTensor?.Dispose();
            _worker?.Dispose();
            _observationTensor = null;
            _maskTensor = null;
            _worker = null;
        }
    }
}
