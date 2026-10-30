using System;
using UnityEngine;

/// <summary>Snapshot serializavel do melhor genoma (Parte C).</summary>
[Serializable]
public class EvolutionSnapshot
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public int seed;
    public int generation;
    public float bestFitness;
    public float[] weights;
    public int populationSize;
    public float mutationRate;
    public float mutationStrength;
    public string savedAt;
}

/// <summary>Persistencia em PlayerPrefs com uma unica chave.</summary>
public static class GenomeStorage
{
    public const string Key = "neuroevolution.best.v1";

    public static bool HasSave => PlayerPrefs.HasKey(Key);

    public static void Save(EvolutionSnapshot snapshot)
    {
        // Copia dos pesos: nunca guardar a referencia ao array da evolucao.
        float[] copy = new float[snapshot.weights.Length];
        Array.Copy(snapshot.weights, copy, copy.Length);
        snapshot.weights = copy;
        snapshot.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        PlayerPrefs.SetString(Key, JsonUtility.ToJson(snapshot));
        PlayerPrefs.Save(); // grava em disco imediatamente
    }

    /// <summary>Tenta carregar. Nunca lanca excecao; devolve mensagem clara.</summary>
    public static bool TryLoad(out EvolutionSnapshot snapshot, out string message)
    {
        snapshot = null;
        if (!PlayerPrefs.HasKey(Key))
        {
            message = "Nenhum save encontrado. Continue treinando e pressione 'Salvar melhor'.";
            return false;
        }

        try
        {
            snapshot = JsonUtility.FromJson<EvolutionSnapshot>(PlayerPrefs.GetString(Key));
        }
        catch (Exception e)
        {
            message = $"Save corrompido ({e.Message}). Apague o save.";
            snapshot = null;
            return false;
        }

        if (snapshot == null || snapshot.weights == null)
        {
            message = "Save invalido (sem pesos). Apague o save.";
            snapshot = null;
            return false;
        }
        if (snapshot.version != EvolutionSnapshot.CurrentVersion)
        {
            message = $"Versao do save ({snapshot.version}) incompativel com a versao atual ({EvolutionSnapshot.CurrentVersion}).";
            snapshot = null;
            return false;
        }
        if (snapshot.weights.Length != NeuralNetwork.WeightCount)
        {
            message = $"Save incompativel: possui {snapshot.weights.Length} pesos, a rede espera {NeuralNetwork.WeightCount}.";
            snapshot = null;
            return false;
        }

        message = $"Save carregado: seed {snapshot.seed}, geracao {snapshot.generation}, fitness {snapshot.bestFitness:F2}, salvo em {snapshot.savedAt}.";
        return true;
    }

    public static void Delete()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}
