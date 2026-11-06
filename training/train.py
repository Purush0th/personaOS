"""Fine-tunes Qwen2.5-3B-Instruct on the PersonaOS dataset with LoRA, and saves it merged.

Runs inside the unsloth container (see README.md in this folder). The loss is on the assistant's
turns only: the system prompt, tools and user words are context, not something to learn to write.
The merged 16-bit model is imported into Ollama, which quantizes it like the stock model.
"""

import argparse
import glob
import json

from unsloth import FastLanguageModel  # first: it patches transformers and trl
from unsloth.chat_templates import train_on_responses_only
from datasets import Dataset
from trl import SFTConfig, SFTTrainer

from ollama_template import render


def load(path: str, tokenizer) -> Dataset:
    rows = []
    for line in open(path, encoding="utf-8"):
        example = json.loads(line)
        # As Ollama renders it at inference, not as the Hugging Face template would.
        rows.append({"text": render(example["messages"], example.get("tools"))})
    return Dataset.from_list(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", default="/work/dataset")
    parser.add_argument("--out", default="/work/model")
    parser.add_argument("--base", default="unsloth/Qwen2.5-3B-Instruct")
    parser.add_argument("--epochs", type=float, default=2)
    parser.add_argument("--rank", type=int, default=16)
    parser.add_argument("--lr", type=float, default=1e-4)
    parser.add_argument("--max-len", type=int, default=10240)
    parser.add_argument("--checkpoints", default="/work/checkpoints", help="per run, so a resume picks up its own")
    args = parser.parse_args()

    model, tokenizer = FastLanguageModel.from_pretrained(args.base, max_seq_length=args.max_len, load_in_4bit=True)
    model = FastLanguageModel.get_peft_model(
        model, r=args.rank, lora_alpha=args.rank, lora_dropout=0,
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"],
        use_gradient_checkpointing="unsloth", random_state=7)

    train, valid = load(f"{args.data}/train.jsonl", tokenizer), load(f"{args.data}/valid.jsonl", tokenizer)
    lengths = [len(tokenizer(t)["input_ids"]) for t in train["text"]]
    print(f"train {len(train)} valid {len(valid)}; tokens max {max(lengths)} mean {sum(lengths) // len(lengths)}")

    trainer = SFTTrainer(
        model=model, tokenizer=tokenizer, train_dataset=train, eval_dataset=valid,
        args=SFTConfig(
            dataset_text_field="text", max_seq_length=args.max_len, per_device_train_batch_size=1,
            gradient_accumulation_steps=8, num_train_epochs=args.epochs, learning_rate=args.lr,
            lr_scheduler_type="cosine", warmup_ratio=0.05, logging_steps=5, eval_strategy="epoch",
            optim="adamw_8bit", weight_decay=0.01, seed=7, output_dir=args.checkpoints, report_to="none",
            # A checkpoint per epoch, so a run that dies (Docker went down mid-run on 2026-11-05)
            # resumes from the last finished epoch instead of starting over.
            save_strategy="epoch", save_total_limit=1))
    trainer = train_on_responses_only(
        trainer, instruction_part="<|im_start|>user\n", response_part="<|im_start|>assistant\n")
    resume = bool(glob.glob(f"{args.checkpoints}/checkpoint-*"))
    trainer.train(resume_from_checkpoint=resume or None)
    print("eval", trainer.evaluate())

    model.save_pretrained_merged(args.out, tokenizer, save_method="merged_16bit")
    print("saved", args.out)


if __name__ == "__main__":
    main()
